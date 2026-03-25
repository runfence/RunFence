# RunFence

> Run each Windows app under its own dedicated local account — fully isolated from your personal files and other apps — without VMs, without password prompts, and without leaving your desktop.

[![License: EL2](https://img.shields.io/badge/license-Elastic%20License%202.0-blue)](LICENSE.md)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey)](https://github.com/runfence/RunFence/releases)

**[⬇ Download the latest release](https://github.com/runfence/RunFence/releases)**

![RunFence — main window with application list](docs/demo.gif)

---

Every non-elevated app you run can silently read your documents, browser profiles, and
credentials. A compromised or untrusted program can exfiltrate your crypto wallet data,
read saved browser passwords, or encrypt your personal files — all without any visible
signs.

RunFence lets you create dedicated local Windows accounts — one per app or trust
level — and launch programs under those accounts with a single click.

Normally, using separate Windows accounts for different apps means typing passwords on
every launch — the built-in `runas` command prompts interactively every time, its
credential caching is unreliable, and the stored credentials are accessible to other apps
running under the same account. RunFence eliminates the friction: accounts and their
passwords are stored in a secure encrypted vault, and then it's a single click.

Standard Windows user folders (Documents, Downloads, Desktop, AppData, and similar) are
already inaccessible to other accounts by default. For paths outside of those defaults —
shared folders, custom data directories, application data elsewhere on the disk — the
**Account ACL Manager** lets you extend isolation with additional deny or allow rules,
configured once and enforced by Windows from that point forward.

The full source code is publicly available under the Elastic License 2.0 — you can
review, build, and audit it to verify that nothing unexpected runs on your system.

---

## Why Native Windows Accounts Beat Driver-Based Sandboxes

| | RunFence | Sandboxie / driver-based |
|---|---|---|
| Kernel driver required | No | Yes |
| Interception layer | None | Yes — bypass vectors exist |
| Enforcement mechanism | Windows kernel itself | Third-party driver |

- **Native Windows boundaries** — isolation is enforced by the OS account model, not by a third-party mechanism inventing its own separation layer on top of Windows
- **No VFS quirks** — driver-based sandboxes need a filesystem virtualization layer to work, which brings compatibility issues and bypass vectors; account separation doesn't require one
- **Enforced by the Windows kernel** — the same mechanism the OS uses to enforce account separation at all levels
- **Cannot be bypassed at the userspace level** — isolation is at the OS account boundary, not at an API interception layer

---

## Use Cases

- **Crypto wallet / trading app** — run under its own account so no other process can touch its data files or private keys
- **Browser profile isolation** — other apps cannot access saved passwords, session cookies, or browsing history
- **AI coding assistant** — run Claude Code (with `--dangerously-skip-permissions`) or similar tools under a dedicated account that only has access to specific project directories; it cannot reach your SSH keys, browser profiles, or wallet files
- **SSH keys and developer secrets** — dedicated dev account that is locked out of your personal files, with access only to the repositories it needs
- **Untrusted or unfamiliar software** — restricted account that cannot reach your documents or credentials
- **Games** — run without trusting the developer with your documents, wallets, or work files; full native performance, unlike a VM; works for games that don't require elevation
- **Elevated app under a dedicated admin account** — launch without typing a password; credentials are stored securely in the vault
- **Ad-hoc run-as for any app** — even apps not pre-configured in RunFence can be launched elevated or under a different account without entering a password; the interactive user confirms the launch at the time it happens
- **Ephemeral accounts** — one-time or disposable runs; the account and its profile are automatically removed after 24 hours

---

## Key Features

### Application Isolation Without Virtualization
Each app runs under a real Windows account. The OS enforces file system boundaries —
no hypervisor, no compatibility issues, no performance overhead.

### AppContainer Sandboxing
For stronger isolation, run apps inside Windows AppContainer profiles instead of plain
account separation. AppContainer goes significantly further: separate IPC namespace,
restricted network access, tightly controlled COM access, and many other OS-level
restrictions that plain account separation does not impose. Most apps are not compatible,
but for those that are it provides a much tighter sandbox — all configured from the
RunFence GUI.

### Low Integrity Mode
Run apps at Windows Low integrity level. Low integrity restricts the app's ability to
interact with other processes, simulate user input, and install keyboard hooks — even
within the same account. A useful extra layer for apps you want to prevent from
interfering with the rest of the session.

### De-Elevation
When launching an app under an admin account, RunFence can strip the elevated
privileges from the process token — the app runs as admin-group member but without
active admin rights. Useful when an app requires an admin account but should not have
full administrative power. Note: Administrators group membership cannot be removed, only
the active elevation.

### Per-App Directory ACLs
Lock or grant access to an app's own directory at the OS level:

- **Deny-mode**: adds deny ACEs for specific accounts — prevents those accounts from
  launching the app, so it can only ever be started from its dedicated isolated account
  and cannot be accidentally run from an unisolated one
- **Allow-mode**: disables permission inheritance on the folder and replaces it with
  fully custom rules — granting access to the app's dedicated account and restricting
  others as needed; gives precise control over who can reach the folder, including
  locations the dedicated account couldn't access by default

### Account ACL Manager
The dedicated tool for data isolation beyond the defaults. Standard Windows user folders
(Documents, Downloads, Desktop, AppData, etc.) are already inaccessible to other
accounts out of the box. For everything else — shared drives, custom data directories,
application data outside the user profile — configure deny or allow rules for any account
or AppContainer on any path. Unlike Windows Explorer, it supports configuring
ACLs using AppContainer SIDs. All rules are tracked: when an account or
container is deleted, its ACL entries are cleaned up automatically.

![Access control configuration for an application](docs/acl.png)

### Encrypted Credential Vault
Account passwords stored locally using DPAPI + AES-256-GCM with Argon2id key
derivation, protected by a PIN and the admin account (DPAPI is account-bound — the
credentials are inaccessible without both). No credentials ever leave the machine. The vault
locks automatically when the GUI is minimized or after idle timeout.

### Startup Security Scanner
At startup, RunFence scans for two classes of issues:

- **Privilege escalation paths** — locations Windows executes automatically at elevated
  privilege that one of your restricted-account apps can write to; that app could plant
  code that later runs as SYSTEM
- **Access control anti-patterns** — configurations that undermine proper per-folder
  isolation, such as accounts having access to an entire drive root instead of specific
  folders

**14 scan categories:**
- Startup folders (machine-wide and per-user)
- Registry Run keys (HKLM + HKCU + all loaded user profiles, including RunOnce and Wow6432Node variants)
- Services — binary paths, parent folders, unquoted path vulnerabilities
- Scheduled tasks — executable paths for all tasks
- Logon and GP scripts — machine-level and per-user group policy scripts
- System DLL injection points — AppInit_DLLs, print monitors, LSA packages, Winlogon, Image File Execution Options (debugger hijacking)
- Parent folder replaceability — flags cases where delete-file + write-to-parent rights allow silently swapping a binary
- Disk roots (non-system drives) — flags accounts with access to an entire drive root; drive-root access conflicts with proper per-folder access control and undermines isolation

For each auto-execute location, it checks whether any non-admin account has write,
append, change-permissions, or take-ownership rights on the file or its containing
directory.

Isolation is only as strong as the boundary — whether that's a restricted account that
can write to a path that later runs as SYSTEM, or one with access broader than it needs.

### Shortcuts & Tray Launching
Create shortcuts anywhere — desktop, Start Menu, or any folder — that launch apps
through an IPC pipe with one click, no interaction with the main window needed.

### App Discovery
When enabled for an account, apps installed into its local AppData that create Start
Menu shortcuts are detected automatically and appear in the tray quick-launch menu.

### Folder Browser & Terminal Quick Access
Explorer cannot run under a different account in the same session. RunFence
provides two one-click toolbar buttons for working within a different account without
switching sessions: a folder browser based on the Windows file open dialog (full Explorer
navigation, otherwise inaccessible) and a terminal — both running under the specified
account.

### Cross-User Drag and Drop
A lightweight bridge process enables dragging files between windows running under
different accounts — something Windows doesn't support natively. For example, drag a
file from your main desktop into a browser running isolated under a dedicated account.

### Ephemeral Accounts
Create a disposable Windows account for a one-time run. RunFence automatically
removes the account and cleans up its profile after 24 hours.

### Account Management
Create and delete Windows accounts directly from the GUI, rotate passwords with one
click, migrate desktop and Explorer settings between accounts, and clean up orphaned
profiles left behind by deleted accounts. One-click shortcuts to install Windows Terminal
and Claude Code into a specified account.

Account restrictions configurable per account:
- **Logon** — when disabled, hides the account from the Windows login screen and blocks
  interactive logon; the account remains usable only through RunFence
- **Network Login** — when disabled, restricts to local access; blocks RDP and SMB
  access from outside the machine (uses LSA policy)
- **Background Autorun** — when disabled, blocks the account from running background
  processes via Task Scheduler and services

![Creating account](docs/account.png)

**SID migration** — when an account is deleted and recreated (Windows assigns a new SID),
scan filesystem ACLs and config data to remap all references from the old SID to the new
one, keeping your isolation setup intact.

### Per-Account Firewall
Control outbound network access per account with Windows Firewall rules scoped by
SID:

- **Allow Internet** — block all outbound internet traffic (allowed by default)
- **Allow Localhost** — block loopback communication (allowed by default)
- **Allow LAN** — block local network traffic to private ranges (allowed by default)

Each setting is independent. A per-account IP/domain allowlist lets specific
destinations bypass the internet block. DNS servers are auto-included when the
allowlist is non-empty so domain resolution keeps working. Rules are re-enforced
on startup and cleaned up on account deletion or SID migration.

### On-Demand Config Files
App entries and ACL rules can be split across multiple encrypted config files. Load
additional configs on demand — from removable media or any path — so that certain
app entries or ACL configurations stay hidden from the tray and UI until explicitly
loaded. Useful when you need a clean separation between what is always visible and
what should only appear in specific contexts.

### Auto-Lock & Idle Timeout
The management GUI auto-locks when minimized or after idle timeout, keeping your
credential vault protected when you step away.

---

## Evaluation Mode

**Free for non-commercial use, no time limit.**

Evaluation mode includes periodic nag reminders. Purchasing a license unlocks:

- **Unlimited app entries** *(evaluation: up to 3)*
- **Unlimited stored credentials** *(evaluation: up to 3)*
- **AppContainer sandboxing** *(evaluation: up to 1)*
- **Account hiding** *(evaluation: up to 1 hidden account)*
- **Auto-lock & idle timeout**
- **Unlimited Internet whitelist entries** *(evaluation: up to 1)*
- **Commercial use**

You can create as many accounts as you like — the limit applies only to hiding them from
the Windows login screen.

---

## Pricing

Licenses are **per machine** — each installation requires its own key, tied to the machine it was activated on. See [PAYMENT.md](PAYMENT.md) for pricing tiers, supporter donations, and payment instructions.

---

## FAQ

**The source is published — does that mean I can do anything with it?**

No. RunFence is **source-available**, not open source in the OSI sense. The source is published for transparency, auditing, and contributions — not to grant the unrestricted freedoms of a true open source license.

**What you can legally do:**
- Use it free of charge for **personal, non-commercial purposes** — no time limit, evaluation feature limits apply
- Build it from source and audit it to verify the binary matches what is published
- Fork it to modify and contribute — pull requests welcome; contributions require signing a CLA

**What is not permitted:**
- **Commercial use without a paid license.** Using RunFence at work or for commercial purposes requires a license.
- **Cracking or bypassing the license key.** Patching the binary or modifying the source to remove or bypass evaluation limits is prohibited.
- **Selling your own licenses.** Replacing the public key to issue your own license keys is explicitly prohibited. You cannot fork this and monetize it yourself.
- **Stripping attribution.** Removing copyright notices or the license is not allowed.

**Does RunFence send any data to the Internet or phone home?**

No. RunFence is entirely offline — no telemetry, no license checks against a server, no update pings. Licensing is validated locally. Nothing leaves your machine.

**Are my stored credentials safe from other apps on my PC?**

Yes. Passwords are encrypted with DPAPI using a PIN-derived entropy value (32 bytes, derived via Argon2id + HKDF from your PIN). DPAPI decryption requires both the correct Windows account session *and* the correct entropy — an app on the same Windows account that tries to call DPAPI without that entropy gets a decryption failure. Without your PIN, the credentials cannot be decrypted. Apps running under *other* accounts — which is the point of RunFence — cannot reach DPAPI-protected data at all. 

**Can an isolated app read my personal files?**

Standard Windows user folders — Documents, Downloads, Desktop, AppData, and similar — are inaccessible to other accounts by default. For paths outside those defaults (shared drives, custom directories), use the Account ACL Manager to add explicit deny rules.

**What happens if I forget my PIN?**

Both stored passwords and app configuration are unrecoverable without the PIN. You can start fresh, but all encrypted data is gone. Keep your PIN somewhere safe. 

But the isolated accounts you created are still there, you can reset their password and use them again.

**What happens to my stored passwords if I migrate RunFence to a different admin account, or the account password is forcibly reset?**

The app configuration (entries, ACL rules, settings) only requires your PIN and is not affected — re-enter your PIN and everything is recovered.

Stored passwords are tied to the Windows account that encrypted them. Two things break this: migrating RunFence to a different Windows account, or having the account password **reset** by someone else (a reset destroys the encryption keys; a normal password *change* by you preserves them). In either case, RunFence detects the problem at startup and the passwords must be re-entered.

**What are the risks of using a simple PIN?**

It depends on how an attacker could access your data, and whether RunFence runs under a dedicated admin account.

**If someone gets the files offline** (steals the machine or copies the files):

- The *app configuration* is protected only by the PIN. An offline attacker can try every candidate PIN — the key derivation is deliberately slow, but a very short numeric PIN has few enough combinations to be brute-forced. A longer PIN is the only protection here.
- *Stored passwords* are additionally tied to the Windows account. An offline attacker cannot use them without also cracking the Windows account password, which is the stronger barrier here.

**If a compromised app runs under the same account as RunFence**: it can read the vault files directly. The PIN is the only thing standing between the attacker and both the configuration and the passwords. A weak PIN is a real risk.

**If RunFence runs under a dedicated admin account** (the intended setup): other apps cannot read the vault files — they are in the admin account's private AppData. A compromised regular app never reaches the vault, making PIN strength far less critical.

**Does running an app under a separate account slow it down?**

No. The app runs as a normal Windows process — no virtualization, no interception layer. Performance is identical to a direct launch.

**Can isolated apps still access the internet?**

By default, yes. The Per-Account Firewall lets you restrict this per account: block internet, LAN, or loopback traffic independently, and add an IP/domain allowlist for selective access.

**Does this work with games?**

It depends on the launcher and anti-cheat system.

**Installation**: most launchers (Epic, GOG, EA App, and others) require elevation to run the game's install scripts, which are provided by the game publisher. The game itself then runs under the isolated account — so isolation applies to the running game, not the install. You are trusting the launcher company to verify those elevated scripts. **Steam is the only major launcher that can itself be installed without elevation.** For individual games within Steam, when elevation is requested you can decline — some games work without it, others do not.

**Running**: games that do not require elevation or kernel-level anti-cheat run normally under an isolated account with full native performance. Kernel anti-cheat drivers (Easy Anti-Cheat, Vanguard, etc.) require system-level access and may not work under a non-admin account; this varies by game.

**Should I give an isolated account admin rights to make games work?**

No — and this applies to any isolated account. Whether UAC is a real security boundary depends on the account type:

- **Non-admin isolated account**: UAC requires entering credentials for a separate admin account that the isolated app does not know — UAC is a genuine barrier.
- **Admin isolated account**: any code running under it can bypass UAC silently using well-known techniques, without any user confirmation.

If you give an isolated account admin rights, any app running under it can silently acquire full admin privileges. The isolation is broken. Keep isolated accounts as standard (non-admin) users — or remove them from the Users group entirely for tighter restriction.

**What is the difference between RunFence and a virtual machine?**

A VM runs a completely separate OS — total isolation but significant overhead, separate software installs, and no native hardware access. RunFence uses Windows account separation within the same OS session: apps run at full native performance, use your GPU directly, and share your desktop. The isolation boundary is the Windows account model rather than a hypervisor.

**What about Microsoft Store (UWP/MSIX) apps?**

Store and UWP apps are tied to the interactive user session and generally cannot be launched under a different Windows account. RunFence's AppContainer sandboxing is a separate mechanism for ordinary Win32 apps and is unrelated to the Store packaging model.

**How do I share files between my main account and an isolated one?**

Place files in a dedicated shared folder and use the Account ACL Manager to grant the isolated account access. For ad-hoc transfers, the built-in cross-user drag-and-drop bridge lets you drag files between windows of different accounts on the desktop.

**Can an isolated app capture keyboard input from my desktop?**

Not if Low Integrity Mode is enabled. Account separation alone does not prevent an isolated app from installing session-wide keyboard hooks — any standard-privilege process can do this regardless of which account it runs under. Low Integrity Mode blocks this: a low-integrity process cannot intercept input from normal windows. AppContainer restricts it further. If the app is not compatible neither with running in AppContainer nor with low-integrity alone, fast user switching is the only way to combat keyboard spying.

**Can isolated apps communicate with or see each other?**

They cannot access each other's files within user profiles. They can see each other's processes and communicate over localhost or through shared paths you have explicitly granted. Low Integrity Mode further restricts cross-process interaction; AppContainer provides separate IPC namespaces for an additional layer.

**Why does RunFence itself need to run as administrator?**

Creating Windows accounts, modifying ACLs, applying per-account firewall rules, and writing logon scripts all require administrator privileges. Isolated apps themselves run under non-admin accounts and do not need elevation.

**Can I use RunFence for apps that require administrator access?**

Yes. Configure the app to run under a dedicated admin account — RunFence launches it elevated using the stored credential, with no UAC prompt.

The opt-in De-Elevation feature can strip the admin privilleges. Note that Administrators group membership itself cannot be removed — only the active elevation is stripped, so it's not the same as running an app under normal account.

**What is AppContainer sandboxing and when should I use it?**

AppContainer is a stronger sandbox that RunFence offers as an alternative to plain account separation. Instead of a separate account, the app runs under your interactive account but inside a container that restricts it much more tightly.

If you want stronger isolation, try AppContainer first. If the app fails or misbehaves, fall back to a normal isolated account.

**My AppContainer app can't access a folder even though the account has permission. Why?**

AppContainer sandboxing adds a second security layer on top of the user account. Each AppContainer profile has its own security ID, and Windows checks both the account and the container identity on every file access. Granting access to the user account alone is not enough — the AppContainer SID must be granted access too.

The standard Windows permission editor has no way to specify an AppContainer SID. Use the Account ACL Manager instead: it shows the container alongside the account and lets you manage both. When you grant access to an AppContainer SID, RunFence automatically mirrors the same permission to the interactive user — without that, the app would still be blocked regardless of the container grant.

**What is a traverse ACL and why does RunFence add them?**

AppContainers require explicit traverse ACEs on every parent directory to access a nested location. For regular isolated accounts, traverse permissions are not strictly necessary, but some tools fail without them.

When you add an allow-rule in the Account ACL Manager, RunFence automatically adds traverse permissions on all parent directories up to the drive root. They are tracked and removed automatically when no longer needed. You do not need to manage them manually.

**What does Low Integrity Mode restrict?**

Low Integrity Mode prevents an app from interacting with the rest of your session: it cannot inject keystrokes or mouse input into other windows, cannot install keyboard or mouse hooks that capture your input, cannot write to most files and registry locations, and cannot send most message types to other processes.

Account separation alone does not prevent keyboard hooking — Low Integrity Mode does. Use it alongside account separation for apps where input isolation matters.

**Can isolated apps access my clipboard?**

With account separation alone, the clipboard is shared across all accounts in the same session — any app can read and write it freely. Low Integrity Mode and AppContainer both restrict clipboard access. For reliable clipboard isolation, use Low Integrity Mode, AppContainer, or separate Windows sessions.

**What is the difference between deny-mode and allow-mode for per-app directory ACLs?**

**Deny-mode** blocks specified accounts from executing files in the app's directory. The main use is ensuring the app can only be launched from its dedicated isolated account — other accounts are explicitly prevented from running the binary directly.

**Allow-mode** replaces the folder's permission inheritance with a fully custom set. You control exactly who has access and at what level.

**Can multiple apps share the same isolated account?**

Yes. Multiple app entries can point to the same account. Each has its own executable and settings, but all share the account's firewall rules, ACL grants, and restrictions. Useful for grouping related apps — a browser and download manager that should share the same isolation boundary, for example.

**What are ephemeral accounts?**

A disposable Windows account. RunFence automatically deletes it — along with its profile, credentials, and permissions — approximately 24 hours after creation.

**How do shortcuts feature work?**

RunFence shortcuts point to a small launcher that forwards the request to RunFence. It starts the app under specific account without any password prompt needed. Shortcuts can be pinned to Start Menu.

**What is App Discovery?**

When enabled for an account, App Discovery detects apps installed that create Start Menu or Desktop shortcuts. Discovered apps appear in the tray quick-launch menu and can be launched directly from there — no app entry configuration or password prompt needed.

**Why is there Folder Browser button?**

Explorer cannot run as a different Windows account in the same session. The Folder Browser button opens a file dialog under the specified account for full folder navigation. In options you can replace folder browser with your favorite file manager e.g. Total Commander.

Note: if you open Explorer window (not file dialog) from inside an isolated account, e.g. from your download manager, the window will be opened under your interactive user account - not the isolated account. Everything you launch from Explorer window will be launched under your interactive account.

**How does the cross-user drag-and-drop bridge work?**

Windows does not support drag-and-drop between windows of different accounts. RunFence bridges this for files and directories: press the copy hotkey (Ctrl+Alt+C by default) to open a drop target window, drag your files or folders onto it, then press the paste hotkey (Ctrl+Alt+V) on the target window under the other account.

**What does auto-lock protect?**

Auto-lock seals the credential vault after the app is minimized. While locked, all configuration and RunAs dialog are inaccessible. App launching via shortcuts and the tray still works — lock protects the configuration interface, not the ability to launch apps.

**What do the account restrictions (Logon, Network Login, Background Autorun) do?**

These are OS-level restrictions, not just RunFence settings:

**Logon** (disabled): hides the account from the Windows login screen and blocks interactive logon. The account still works through RunFence.

**Network Login** (disabled): blocks RDP and SMB access from other machines. The account can only be used locally through RunFence.

**Background Autorun** (disabled): prevents Task Scheduler and Windows Services from running under the account.

**When I delete an account, what is removed and what is left behind?**

The Windows account, firewall rules, stored credentials, and ACL granted through RunFence are all removed.

The **user profile folder** (`C:\Users\<name>`) is removed as well when possible, but may be locked by the system and left behind. Use the Orphaned Profiles button to clean it up later.

**What are on-demand config files (.rfn)?**

A separate encrypted file containing additional app entries that you load explicitly. Entries appear in the tray and app list for the current session and disappear when RunFence restarts — they are not persisted.

The use case is to store sensitive app entries (a crypto wallet launcher, for example) on a USB drive. Load when needed; unplug and they vanish from the UI.

**Can I move my RunFence configuration to a different machine?**

The main config (`%APPDATA%\RunFence\config.dat`) is portable — move it to another machine and RunFence will ask for your PIN to decrypt it. App entries, ACL rules, and settings are all preserved.

However, app entries and ACL rules reference accounts by their Windows SID, and accounts on the new machine will have different SIDs. After recreating your isolated accounts, use SID migration (Account menu) to scan your filesystem and RunFence configuration for references to the old SIDs and rewrite them to the new ones. The same applies whenever an account is deleted and recreated on the same machine.

Stored account passwords (`%LOCALAPPDATA%\RunFence\credentials.dat`) are tied to the Windows account and machine they were created on and cannot be moved. You will need to re-enter them on the new machine.

---

## Requirements & Installation

- Windows 10 or later (x64)
- .NET 8 Desktop Runtime
- Administrator privileges (the management GUI runs elevated)

**[⬇ Download the latest release from GitHub Releases](https://github.com/runfence/RunFence/releases)**

---

## Contributing

Issues and pull requests are welcome for bug reports and feature suggestions.
Contributors must sign a CLA (Contributor License Agreement) for their code to be
included in the project.

---

## Building from Source

The full source code is available for review and auditing. You can build and run your
own binary to verify that what you're running matches what is published.

```bash
dotnet build RunFence.sln -v quiet
dotnet test src\RunFence.Tests\RunFence.Tests.csproj --no-build
```

Requires: .NET 8 SDK, Windows.

---

## Verifying the Licensing System

RunFence uses offline ECDsa P-256 license keys. The signing public key is embedded in
the binary. To detect whether the licensing system has been tampered with, compare the
**Licensing Key Fingerprint** shown in About → License section against the official
fingerprint:

```
39:59:B7:0A:23:39:29:93:06:D3:98:84:E3:55:37:4E
```

A mismatch means the embedded key was tampered with — you are likely running a cracked
build, which is illegal. Do not trust this binary.

---

## Third-Party Notices

See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

---
## Development Tools

```
dotnet tool install --global wix
wix extension add --global WixToolset.UI.wixext/6.0.0
```

---

## Contact / Security

**Email:** [runfencedev@gmail.com](mailto:runfencedev@gmail.com)
**GitHub:** https://github.com/runfence/RunFence

To report a security vulnerability, please email directly rather than opening a public
issue.
