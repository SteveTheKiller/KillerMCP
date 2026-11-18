# Security

## Reporting a problem

Please report security problems privately through GitHub's private vulnerability reporting on this repository (Security tab, "Report a vulnerability"). Do not open a public issue for them.

Include the KillerMCP version, the client you use it with, and the steps that show the problem. You will get a reply once it has been looked at, and a fix ships in the next release.

## Supported versions

Only the latest release gets security fixes. KillerMCP checks for updates itself, so staying current is the fix path.

## What KillerMCP does to stay safe

- Installs per user and refuses to run setup or the configurator as administrator.
- Release installers and the programs inside them are signed by the KillerMCP publisher, and every channel checks that signature before it publishes.
- Release workflows pin every action, module and tool, and publish only from a protected release environment.
