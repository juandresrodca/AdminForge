# Discoverability

Where AdminForge is listed, where it has been submitted, and why it has not been sent
anywhere else yet.

Each list has entry rules, and this page checks AdminForge against them before anything
is submitted. An entry that breaks a list's rules gets closed — and on awesome-selfhosted,
a machine-generated one gets the submitter banned. Progress on both is tracked in
[#35](https://github.com/juandresrodca/AdminForge/issues/35).

---

## Submissions

| List | Status | Date | Link |
|---|---|---|---|
| NoSignups (formerly FckSignups) | Not submitted — needs a public instance | Checked 2026-09-13 | [#35](https://github.com/juandresrodca/AdminForge/issues/35) |
| awesome-selfhosted | Not submitted — needs a release four months old | Checked 2026-09-13 | [#35](https://github.com/juandresrodca/AdminForge/issues/35) |

Add a row the day something is sent, with the date and a link to the issue or pull
request it became.

---

## NoSignups

[nosignups.net](https://nosignups.net) ·
[BraveOPotato/FckSignups](https://github.com/BraveOPotato/FckSignups) — open-source tools
you can use in the browser without an account.

### Where AdminForge stands

Checked against their README and submission template on 13 September 2026.

| Their rule | AdminForge |
|---|---|
| Open source | **Yes** — MIT |
| Works without creating an account | **Yes** — there are no accounts at all |
| `url` is a direct link to the tool, usable in the browser | **No** — there is no public instance yet, and none of the 262 entries they list links to a repository instead |
| Description under 140 characters | **Yes** — the draft below is 115 |
| Three to five tags | **Yes** — five |
| Sent through the **Request to Add a Tool** issue template, or the site's *Submit a tool* button | Their intake is issues, not pull requests; the recent merged pull requests are all changes to the site itself |

### What unblocks it

A public instance. Any small container host will run
`ghcr.io/juandresrodca/adminforge:latest`; leave `AllowPrivateTargets` off, set
`InstanceBanner` so visitors know it is a demo, and make it the repository homepage.

### Entry, ready for their template

```text
Name: AdminForge
Description: Sysadmin and security toolbox: subnets, TLS certificates, DNS, SPF/DKIM/DMARC and Windows error codes in one place.
URL: <the public instance>
Tags: sysadmin, networking, security, dns, certificates
Github: https://github.com/juandresrodca/AdminForge
Category: Utilities
```

IT-Tools, the nearest thing they already list, sits under *Development* and CyberChef
under *Privacy*. *Utilities* fits sysadmin work better than either, but it is the
maintainers' call.

---

## awesome-selfhosted

[awesome-selfhosted.net](https://awesome-selfhosted.net) · entries live in
[awesome-selfhosted/awesome-selfhosted-data](https://github.com/awesome-selfhosted/awesome-selfhosted-data).
The main repository is generated from that data and does not take additions directly.

### Where AdminForge stands

Checked against their contributing guide and pull request checklist on 13 September 2026.

| Their rule | AdminForge |
|---|---|
| First released more than four months ago | **No** — no tagged release exists. `[0.1.0]` in the changelog was never tagged, and their count starts at the release, not the first commit |
| Actively maintained | **Yes** |
| Working installation instructions | **Yes** — one `docker run` line, plus compose and from source |
| Licence on their list | **Yes** — MIT |
| Not already listed on awesome-sysadmin | **Yes** |
| Not a cloud-dependent service, a library, a PaaS, or a Dockerised port of another project | **Yes** — none of those apply |
| Demo link, if given, is an interactive demo | Optional; the NoSignups instance would serve |
| Description under 250 characters, sentence case, without "open-source", "free" or "self-hosted" | **Yes** — the draft below is 175 |
| No machine-generated submissions that ignore the guidelines | Their stated penalty is a ban, so this entry should be written and checked by hand |

### What unblocks it

A published release, then four months. `gh release create v0.1.0 --generate-notes`
starts the clock; tagged on 13 September 2026, the earliest submission date would be
**13 January 2027**. A later first release moves that date with it.

### Entry, for `software/adminforge.yml`

```yaml
name: AdminForge
website_url: https://github.com/juandresrodca/AdminForge
source_code_url: https://github.com/juandresrodca/AdminForge
description: Browser-based toolbox for sysadmin and security work, including a subnet calculator, TLS certificate checker, DNS and SPF/DKIM/DMARC lookups, and a Windows error code decoder.
licenses:
  - MIT
platforms:
  - C#
  - Docker
tags:
  - Miscellaneous
  - Network Utilities
```

- **Tags.** Only the first tag shows in their single-page view. CyberChef, the closest
  comparable entry, is filed under *Miscellaneous*; *Network Utilities* covers the
  network tools but not the rest.
- **Platforms.** `C#` and `Docker` match how other .NET entries such as Ombi and slskd
  declare themselves, and are what installing AdminForge actually needs.
- **`depends_3rdparty`** is left at its default of `false`. AdminForge runs without any
  outside service; only the RDAP lookup reaches out, to rdap.org.
- Add `demo_url` once a public instance exists.

---

## Keeping this page honest

- Re-read a list's rules before submitting. Both change, and every check above is dated.
- Both lists look hard at privacy claims. The README's opening lines were tightened on
  13 September 2026 so that each one survives checking: eight of the twelve tools run on
  the server, so "nothing leaves the browser" is not a claim AdminForge makes.
