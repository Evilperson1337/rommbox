# Troubleshooting

Use this page to resolve common problems. If you still need help, gather logs and share them with support.

## Plugin not showing in LaunchBox

**Symptoms**

- No **RomM** entry under **Tools**.

**Possible causes**

- Plugin files are not in the correct folder.
- LaunchBox was not restarted after installing.

**Fix**

1. Close LaunchBox.
2. Verify files are in `LaunchBox\Plugins\RomMbox\`.
3. Reopen LaunchBox.

## “Server not configured”

**Symptoms**

- Platforms or Games page says the server is not configured.

**Fix**

1. Go to **Connection** and enter server details.
2. Select **Test**.
3. Select **Save**.
4. Return to **Platforms** and **Games** and select **Refresh** if needed.

## Connection failed

**Symptoms**

- Status stays **Not Connected** after testing.

**Fix**

- Verify server address and port.
- Verify username and password.
- If your server uses a self‑signed certificate, enable **Ignore Certificate** and test again.

## Platforms won’t load

**Symptoms**

- Platforms list is empty.

**Fix**

- Confirm your connection is saved and shows **Connected**.
- Select **Refresh** on the Platforms page.

## Import fails

**Symptoms**

- Import completes with failures.
- “Failed to import selected games.” message.

**Fix**

1. Review the Import Report for failed items.
2. Check logs (see **Gather logs** below).
3. Verify you have mapped platforms correctly.

## Install or uninstall fails

**Symptoms**

- Install dialog says “services unavailable.”
- Uninstall does not remove files.

**Fix**

- Restart LaunchBox.
- Verify your connection and platform settings.
- Check file permissions on your install directory.

## Save downloads not working

**Symptoms**

- Save files do not appear under `LaunchBox\Saves`.

**Fix**

- Make sure **Saves** is checked for the game before importing.
- Confirm your RomM server provides save data.
- Check logs for save download errors.

## Performance issues

**Symptoms**

- Long import times or slow UI.

**Fix**

- Import one platform at a time.
- Use filters and “Hide Skipped” to reduce the number of rows.

## Gather logs

Logs are stored at:

- `LaunchBox\Plugins\RomMbox\system\RomM.Plugin.log`

When reporting issues, include:

- The log file
- What you were doing
- The time the issue happened
