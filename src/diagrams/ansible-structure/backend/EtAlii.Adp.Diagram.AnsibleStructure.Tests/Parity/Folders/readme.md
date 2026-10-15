# Ansible structure finding folders

One folder per finding of `ansible-structure-disl-fbl`'s requirements marked *read* (Requirement 1.2), each read by the parity transcript (`../AnsibleTranscript.cs`) beside the four trees under `../../Fixtures/`. Every file here was written by hand for this repository. Nothing here is written by a test: each folder is read from a copy in a temporary folder.

What git cannot hold is made in that copy by `../AnsibleFindingFolders.cs` and listed under `made` in the transcript: the empty folders of B2, the binary of B3, the missing folder of B5, the registration with stored positions of B9 and the links of B11. `b5-not-there` has no tree here at all.

`.gitattributes` marks everything here `-text`, so the bytes the transcript hashes are the bytes on every platform.

| Folder | Finding | What it shows today |
| --- | --- | --- |
| `a5-first-plays` | A5 | Two playbooks with two plays each: the first plays are `play:one.yml#0` and `play:two.yml#0`, not one `play:0`. |
| `a7-play-the-fool` | A7 | A play written as `play the fool` is reported by its name; an unnamed one is labelled `play 2` and reported as "A play". |
| `a8-a12-unreadable-and-order` | A8, A12 | Two unreadable files in two folders, one finding each, with the path in the sentence; then the missing targets in derivation order (role, import, dependency, include), then unmatched hosts, then an empty role. |
| `a15-role-named-twice` | A15 | A role listed twice in one `roles:` and imported and included by one role: two relations with one id each time, both delivered. The play's own `import_role` under `tasks:` yields nothing, because a play's tasks are not read. |
| `b2-empty-folders` | B2 | An empty role (hollow, reported), a role with only an empty `meta/` (not hollow) and an empty `group_vars/` (a node with 0 files). |
| `b3-large-binary` | B3 | An 8 MiB file under a role's `files/` is counted. That it is not opened cannot show in a transcript. |
| `b4-ini-and-broken-hosts` | B4 | An INI `hosts` read as INI; a broken `hosts.yml` at the root reported (it is read as a candidate playbook too) and one under `inventories/` silent; a repeated key refused by YamlDotNet. |
| `b5-not-there`, `b5-nothing-ansible` | B5 | Nothing delivered and no finding, whether the folder is missing or holds nothing Ansible. |
| `b6-reading-order` | B6 | `site.yml` is read before `playbooks/aaa.yml`, which sorts first. |
| `b7-missing-and-expression` | B7 | Relations with a missing and with an expression target, delivered without a target; only the missing ones reported. |
| `b9-half-saved` | B9 | A position stored for a playbook that does not parse is kept when another node is moved. |
| `b11-links` | B11 | A linked file is read as a playbook; a linked folder under `roles/` is skipped. |
| `b12-1-ipv6-loopback` | B12 | `hosts: ::1` is reported as unmatched; `127.0.0.1` is not. |
| `b12-2-targets-declaration` | B12 | A `Targets` relation says "Declared in `:1`", directive `roles:`, and selects to an empty path. |
| `b12-3-play-include-tasks` | B12 | A play's `include_tasks` and `import_tasks` naming no file are silent. |
| `b12-4-single-play-tint` | B12 | A single-play playbook carries no play index, and the next playbook's plays start at 1. |
| `b12-5-root-inventories` | B12 | Two root inventory files each get the root's `group_vars/`, delivered twice with one id. |
| `b12-6-extensionless-inventory` | B12 | A file with no extension in an inventory folder contributes its groups. |
| `b12-7-role-counts` | B12 | A role's `handlers`, `defaults` and `vars` count every file; `tasks` counts YAML only. |

What these folders do not demonstrate: that a counted file is never opened (B3), anything about watching or settling, and anything a real Ansible run would say about them. None is validated by Ansible.
