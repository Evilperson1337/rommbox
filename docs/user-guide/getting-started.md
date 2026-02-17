# Getting Started

This section shows how to access the RomM plugin in LaunchBox and complete a basic first-time setup.

## Where to find the plugin

1. Open LaunchBox.
2. Go to **Tools → RomM**.
3. The RomM window opens with tabs for Connection, Platforms, and Games.

   <img src="../_assets/images/romm_tools_menu.png" alt="RomM Connection Screen">

## First-time setup (happy path)

1. Open **Tools → RomM**.
2. In the **Connection** page, enter:
   - Server Address
   - Port (default is 443)
   - Username
   - Password
3. Select **Test**.
4. If the status shows **Connected**, select **Save**.
5. Go to the **Platforms** page and map your RomM platforms to LaunchBox platforms.
6. Select **Save**.
7. Go to the **Games** page (Import) and select a platform to load ROMs.
8. Choose **Import** or **Install** actions and run the import.

   <img src="../_assets/images/romm_server_config.png" alt="RomM Connection Screen" width="50%">

## What success looks like

- The status text shows **Connected** in the Connection page.
- The Platforms page shows your RomM platforms with LaunchBox platform selections.
- The Games page lists ROMs from your RomM server and lets you import them.

## Common first-run issues

### “Server not configured” messages

- Make sure you saved a working connection on the Connection page.
- Return to **Connection**, test, and **Save** again.

### “Connection failed”

- Check your server address, port, username, and password.
- If your server uses a self‑signed certificate, enable **Ignore Certificate** and test again.

### No platforms appear

- Confirm the connection is saved and shows **Connected**.
- Return to the Platforms page and select **Refresh**.

> **Tip:** If you change your server URL or credentials, revisit Platforms and Games to refresh data.
