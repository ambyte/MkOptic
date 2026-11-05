#!/bin/sh
# Refresh the desktop database so that MkOptic shows up in "Open with" for Markdown files.
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications || true
fi
exit 0
