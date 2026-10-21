# podman-helper

Windows CLI that fixes the gaps of the Podman Hyper-V machine's 9p file sharing for the paths that need it.

The Podman machine on Hyper-V shares Windows folders with the Linux VM over 9p. That share has no POSIX
execute bit and no file locking (`flock` returns *Function not implemented*), which crashes software such
as the Visual Studio remote debugger (`vsdbg`) or anything that locks its files (Kerberos keytabs, local databases).

`podman-helper` keeps 9p as the default and, for a short list of paths, puts a real ext4 copy on top of the
same path inside the VM, kept in sync with Windows by [Mutagen](https://mutagen.io):

1. `C:\Users\me\vsdbg` is visible in the VM as `/mnt/c/Users/me/vsdbg` through 9p (Podman's own mapping).
2. The helper syncs the folder with Mutagen into `/var/lib/podman-helper/sync/<id>` (ext4, inside the VM).
3. After the first full sync it runs `mount --bind` of that copy over `/mnt/c/Users/me/vsdbg`.
4. Containers that bind-mount the path (as Visual Studio does for `/remote_debugger`) get the ext4 copy,
   with execute bits and working locks. Paths not on the list keep the plain 9p view.

A hook in the Visual Studio docker-compose project (`.dcproj`) runs the helper on every F5 and waits for it,
so compose never starts against a half-synced path.

## Install

Prerequisites on Windows: Podman with a Hyper-V machine, the Windows *OpenSSH Client* optional feature
(present by default on Windows 10/11), and the .NET runtime (any version 8 or newer; the Visual Studio
.NET SDK covers it). Mutagen v0.18.1 is bundled and extracted on first use to
`%LOCALAPPDATA%\podman-helper\mutagen\0.18.1\`; it uses its own data directory, so a Mutagen you installed
yourself is not touched.

```powershell
dotnet tool install -g podman-helper          # from the NuGet package built by CI (artifacts/*.nupkg)
# or drop podman-helper.exe (single-file build) somewhere on PATH
podman-helper doctor
```

## Files

Entries can be folders or single files (for example a Kerberos keytab). Two `filemap-list.txt` files with the same format. The global one at
`%USERPROFILE%\.podman-helper\filemap-list.txt` covers what every project needs (vsdbg); the project one
sits next to the `.dcproj`. `mount` takes the union; a project entry with the same path overrides the
global one, and `!path` removes a global entry for that project.

```
# <windows-path> [key=value ...]      ~ and %VAR% expand, relative paths resolve from the file's folder
~\vsdbg                         mode=one-way-replica  x=**/vsdbg,**/*.sh
.\src\Api\bin\Debug\net10.0     mode=one-way-replica
.\data                          mode=two-way-safe ignore=*.log,**/tmp
~\myuser.keytab                 # single file
!~\.nuget\packages
```

| Key | Meaning | Default |
| --- | --- | --- |
| `mode` | `one-way-replica` (Windows is the source of truth), `one-way-safe`, `two-way-safe` (container writes flow back, conflicts are reported), `two-way-resolved` (Windows wins) | `one-way-replica` |
| `x` | Globs that get `chmod +x` in the VM after sync (Windows has no execute bit to copy) | none |
| `ignore` | Mutagen ignore patterns (`.git` is always ignored) | none |
| `target` | VM path to bind over, when the automatic `/mnt/<drive>/...` mapping is not what a container uses | computed |
| `wait` | `false` lets `mount` return before this entry's first sync finishes | `true` |

Nested entries (one inside another) are rejected: Mutagen forbids nested sync roots and the inner bind
mount would be hidden by the outer one.

## Commands

| Command | What it does |
| --- | --- |
| `init [--project <dir>] [--global-only] [--force]` | Creates the global file, the project file, `podman-helper.targets` next to the `.dcproj` and the one `<Import>` line in it. Never overwrites without `--force`. |
| `doctor [--project <dir>]` | Checks podman, the machine, ssh, Mutagen and the lists. |
| `mount [--project <dir>] [--timeout <s>] [--no-start]` | What Visual Studio runs: sync every entry and bind-mount it. Exit 0 only when everything is in place. |
| `status [--project <dir>]` | Sync and mount state per entry. |
| `unmount [--project <dir>] [--all] [--purge]` | Removes the bind mounts and pauses the sessions; `--purge` terminates them and deletes the copies. |
| `flush [--project <dir>]` | Forces a sync cycle and waits for it. |

Global options: `--machine <name>`, `--ssh-user <user>` (default `root` on a rootful machine), `--verbose`.

### What `mount` does

1. Loads and merges the lists; stops on any error.
2. Reads `podman machine inspect` for the SSH port, user and key; starts the machine if it is stopped.
3. Writes a managed `Host podman-helper-<machine>` block into `%USERPROFILE%\.ssh\config` so `ssh` and Mutagen share it.
4. Starts the Mutagen daemon (own data directory under `%LOCALAPPDATA%\podman-helper\mutagen\data`).
5. Creates the sync directories in the VM and one Mutagen session per entry (`ph-<id>`), or resumes it.
6. Waits (`mutagen sync flush`) until each entry with `wait=true` has completed a cycle with no conflicts or errors.
7. Applies the `x=` globs with `chmod +x`.
8. `mount --bind /var/lib/podman-helper/sync/<id> <target>` for every entry, replacing a stale bind.

Everything is idempotent; the second run with nothing changed takes a couple of seconds. The bind mounts
do not survive a machine restart (the copies do), so the next `mount` or F5 puts them back.

## Visual Studio

`init` adds `podman-helper.targets` next to the `.dcproj`. Visual Studio's `Microsoft.Docker.Sdk` builds
the service projects (`DockerBuildServiceReferences`), builds the compose project and then runs
`compose up` from its `DockerPrepareForLaunch` target on F5. The hook runs between the service builds and
the launch, only when Visual Studio is launching (the `launch.sem` marker), and fails the build if the
sync is not complete:

```xml
<Target Name="PodmanHelperMount"
        AfterTargets="DockerBuildServiceReferences"
        BeforeTargets="DockerComposeProjectBuild;DockerPrepareForLaunch"
        Condition="... Exists('$(DockerIntermediateOutputPath)\launch.sem')">
  <Exec Command="podman-helper mount --project &quot;$(MSBuildProjectDirectory)&quot; --timeout 180" />
</Target>
```

Knobs: `PodmanHelperEnabled=false` turns it off on a machine, `PodmanHelperAlwaysRun=true` also runs it on
plain builds, `PodmanHelperTimeout`, `PodmanHelperExe`.

## Caveats

- **Sync, not a mount.** Changes made on Windows reach the VM after a sync cycle (well under a second with
  file watching); the initial copy of a large tree takes minutes. Keep the NuGet cache off the list unless a
  project needs it in the container.
- **Single files.** Mutagen replaces a changed file with a new one, and a bind mount keeps showing the
  old one until the next `mount` (or F5) re-binds it. Fine for keytabs and config files that rarely change;
  sync the folder instead if the file changes while containers run. Relative paths resolve from the folder
  of the filemap file, so write `~\myuser.keytab` or a full path.
- **Execute bit.** Files created on Windows arrive without it; use `x=` globs. The helper re-applies them on
  every `mount`.
- **Two-way entries** can conflict; `status` shows conflicts and `mount` fails on them. Never two-way sync a
  database file.
- **Locking** works on the copy (ext4). A lock in the VM means nothing to Windows and vice versa.
- **Ordering.** A container captures the VM path at start; `mount` must run before `compose up`, which the
  hook guarantees. Containers started by hand before the first `mount` need a restart.
- **Disk.** Every listed folder is duplicated in the machine; `status` shows sizes, `unmount --purge` cleans up.

## Development

```
dotnet build
dotnet test
dotnet publish src/PodmanHelper/PodmanHelper.csproj -c Release -r win-x64 -p:PublishSingleFile=true
```

The build downloads the pinned Mutagen zip once into `src/PodmanHelper/obj/mutagen/` and verifies its
SHA-256; pass `-p:PodmanHelperSkipMutagenDownload=true` to build without it (the tool then needs
`mutagen` on PATH). Mutagen is MIT licensed; its license ships in `third_party/mutagen/LICENSE` and next
to the extracted binary.
