stamp the kernel with what it was built from, and let it say when it is stale

the installed tool and the built solution are two different artifacts, and nothing connects them.
a green build and a green suite say nothing about whether `ailedger` has the change. this cost real
confusion twice in one session: a progress-streaming fix was written, built and tested, and the
launch log still read 0 bytes because the installed command predated it; later the Archive help
prose was missing for the same reason.

the failure is silent, which is what makes it worth fixing rather than remembering.

## what it should do

embed the commit and the build time at pack time, expose them, and compare when running inside a
ledger home:

    ailedger version
      2.0.0  built 2026-09-07T20:31Z from 8e1ac0d

    ailedger status --task T
      warning: this ailedger was built from 8e1ac0d; the ledger home is at 4f2a91b.
      run `ailedger self-update` or `dotnet pack && dotnet tool update`.

warn, do not refuse. a stale tool still reads a ledger correctly, and a kernel that refuses to run
because a source tree moved is a kernel that gets uninstalled. the warning belongs on mutations and
on `provider launch` above all — a launch hands a manifest to an agent, and a stale launcher is how
an agent gets briefed by yesterday's rules.

skip the check when there is no ledger home above the working directory, and when the source tree
is dirty, since a dirty tree has no commit to compare against.

## the reinstall friction, which is the same problem

`dotnet tool install` refuses with "Tool 'ailedger.cli' is already installed" whenever the version is
unchanged, and the version is hardcoded, so every rebuild collides. `dotnet tool update` refuses the
identical version too. today that meant uninstall-then-install every time, and forgetting it is how
the tool went stale in the first place.

two halves:

- derive the patch version from the commit count or the build timestamp, so each pack is a distinct
  version and `dotnet tool update` does the right thing
- ship `scripts/install.sh` that packs, uninstalls if present, installs, and prints the resulting
  stamp, so there is one command and it is idempotent

## why the version stamp and not just the script

the script fixes the friction. the stamp fixes the silence. a person who never runs the script still
gets told, and that is the failure mode that actually happened — not being unable to update, but not
knowing an update was needed.
