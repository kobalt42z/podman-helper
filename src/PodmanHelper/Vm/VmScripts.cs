using System.Text;
using PodmanHelper.Config;

namespace PodmanHelper.Vm;

/// <summary>Bash snippets executed inside the podman machine. All of them are idempotent.</summary>
public static class VmScripts
{
    /// <summary>Creates the sync directories, owned by the SSH user so the Mutagen agent can write them. Needs sudo.</summary>
    public static string Prepare(IEnumerable<FileMapEntry> entries, string sshUser)
    {
        var sb = new StringBuilder();
        sb.Append("mkdir -p ").Append(VmPaths.SyncRoot).Append('\n');
        sb.Append("touch ").Append(VmPaths.MountsFile).Append('\n');
        foreach (var e in entries)
        {
            var dir = VmPaths.ShellQuote(e.VmSyncDirectory);
            sb.Append("mkdir -p ").Append(dir).Append('\n');
            sb.Append("chown ").Append(VmPaths.ShellQuote(sshUser)).Append(' ').Append(dir).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Marks the files matching the entry's x= globs executable. Needs sudo (files may be owned by root after a mode change).</summary>
    public static string ApplyExecBits(IEnumerable<FileMapEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            if (e.ExecGlobs.Count == 0) continue;
            var dir = VmPaths.ShellQuote(e.VmSyncPath);
            var tests = string.Join(" -o ", e.ExecGlobs.Select(g => GlobToFindTest(e.VmSyncDirectory, g)));
            sb.Append("find ").Append(dir).Append(" -type f \\( ").Append(tests).Append(" \\) ! -perm -u+x -exec chmod a+x {} +\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Translates a filemap glob into a <c>find -path</c> test. <c>**/vsdbg</c> matches a file named vsdbg at any
    /// depth, <c>bin/*.sh</c> matches relative to the entry root.
    /// </summary>
    internal static string GlobToFindTest(string root, string glob)
    {
        var g = glob.Replace('\\', '/').TrimStart('/');
        string pattern;
        if (g.StartsWith("**/", StringComparison.Ordinal))
        {
            pattern = "*/" + g[3..].Replace("**", "*");
        }
        else
        {
            pattern = root.TrimEnd('/') + "/" + g.Replace("**", "*");
        }
        return "-path " + VmPaths.ShellQuote(pattern);
    }

    /// <summary>
    /// Shell test that is true when <paramref name="dst"/> currently shows the synced copy: a bind mount exposes the
    /// source's device and inode, which also catches a file entry whose copy Mutagen has since replaced (new inode).
    /// </summary>
    private static string SameInode(string src, string dst) =>
        $"[ \"$(stat -c %d:%i {src} 2>/dev/null)\" = \"$(stat -c %d:%i {dst} 2>/dev/null)\" ]";

    private static string MountPointOf(string dst) =>
        $"mp=$(findmnt -n -o TARGET --target {dst} 2>/dev/null | tail -n1 || true)\n";

    /// <summary>Bind-mounts every synced copy (folder or file) over its VM target, replacing a stale bind. Needs root.</summary>
    public static string Mount(IEnumerable<FileMapEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("touch ").Append(VmPaths.MountsFile).Append('\n');
        foreach (var e in entries)
        {
            var src = VmPaths.ShellQuote(e.VmSyncPath);
            var dst = VmPaths.ShellQuote(e.VmTarget);
            var line = VmPaths.ShellQuote(e.VmSyncPath + " " + e.VmTarget);
            sb.Append("[ -e ").Append(src).Append(" ] || { echo ").Append(VmPaths.ShellQuote("synced copy missing: " + e.VmSyncPath)).Append(" >&2; exit 3; }\n");
            if (e.IsFile)
            {
                sb.Append("[ -e ").Append(dst).Append(" ] || { mkdir -p \"$(dirname ").Append(dst).Append(")\" && touch ").Append(dst).Append("; }\n");
            }
            else
            {
                sb.Append("mkdir -p ").Append(dst).Append('\n');
            }
            sb.Append("if ").Append(SameInode(src, dst)).Append("; then echo ").Append(VmPaths.ShellQuote("ok " + e.Id)).Append("; else\n");
            sb.Append("  ").Append(MountPointOf(dst));
            // a stale bind sits exactly on the target (old copy, or a file Mutagen replaced): take it off first
            sb.Append("  if [ \"$mp\" = ").Append(dst).Append(" ]; then umount ").Append(dst).Append(" || true; fi\n");
            sb.Append("  mount --bind ").Append(src).Append(' ').Append(dst).Append('\n');
            sb.Append("  echo ").Append(VmPaths.ShellQuote("mounted " + e.Id)).Append('\n');
            sb.Append("fi\n");
            sb.Append("grep -qxF ").Append(line).Append(' ').Append(VmPaths.MountsFile)
              .Append(" || echo ").Append(line).Append(" >> ").Append(VmPaths.MountsFile).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Prints one line per entry: <c>&lt;id&gt; mounted|stale|none &lt;size&gt;</c>. No root needed.</summary>
    public static string Status(IEnumerable<FileMapEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            var dst = VmPaths.ShellQuote(e.VmTarget);
            var src = VmPaths.ShellQuote(e.VmSyncPath);
            sb.Append(MountPointOf(dst));
            sb.Append("size=$(du -sh ").Append(src).Append(" 2>/dev/null | cut -f1 || true)\n");
            sb.Append("if ").Append(SameInode(src, dst)).Append("; then state=mounted; elif [ \"$mp\" = ").Append(dst).Append(" ]; then state=stale; else state=none; fi\n");
            sb.Append("echo \"").Append(e.Id).Append(" $state ${size:--}\"\n");
        }
        return sb.ToString();
    }

    /// <summary>Removes the bind mounts (and, with purge, the synced copies). Needs root.</summary>
    public static string Unmount(IEnumerable<FileMapEntry> entries, bool purge)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            var dst = VmPaths.ShellQuote(e.VmTarget);
            var dir = VmPaths.ShellQuote(e.VmSyncDirectory);
            sb.Append(MountPointOf(dst));
            sb.Append("if [ \"$mp\" = ").Append(dst).Append(" ]; then umount ").Append(dst).Append(" && echo ").Append(VmPaths.ShellQuote("unmounted " + e.Id)).Append("; fi\n");
            sb.Append("if [ -f ").Append(VmPaths.MountsFile).Append(" ]; then grep -vxF ").Append(VmPaths.ShellQuote(e.VmSyncPath + " " + e.VmTarget)).Append(' ')
              .Append(VmPaths.MountsFile).Append(" > ").Append(VmPaths.MountsFile).Append(".tmp || true; mv ").Append(VmPaths.MountsFile).Append(".tmp ").Append(VmPaths.MountsFile).Append("; fi\n");
            if (purge)
            {
                sb.Append("rm -rf ").Append(dir).Append(" && echo ").Append(VmPaths.ShellQuote("purged " + e.Id)).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>Facts for <c>doctor</c>. No sudo.</summary>
    public const string Doctor =
        "echo \"os=$(. /etc/os-release && echo \"$PRETTY_NAME\")\"\n" +
        "echo \"user=$(id -un) uid=$(id -u)\"\n" +
        "if sudo -n true 2>/dev/null; then echo sudo=ok; else echo sudo=missing; fi\n" +
        "echo \"mnt_c=$(findmnt -n -o FSTYPE --target /mnt/c 2>/dev/null || echo none)\"\n" +
        "echo \"varlib_free=$(df -h --output=avail /var/lib 2>/dev/null | tail -n1 | tr -d ' ')\"\n" +
        "for t in findmnt flock mountpoint scp; do command -v $t >/dev/null && echo \"tool_$t=ok\" || echo \"tool_$t=missing\"; done\n";
}
