# Connection Tab
Use this tab to configure your connection to the RomM Server.

1. Open **Tools → RomM → Connection**.
2. Fill in the fields using the section below if you need it.
3. Hit **Test** to confirm your connection to the server.
    - Currently only basic authentication (no OIDC/OAuth yet)
4. Hit **Save** to save the connection (uses Windows Credential Management for secure storage).

Upon successful save, the other tabs should unlock and the status should show *Connected*

<img src="../_assets/images/romm_server_config.png" alt="RomM Connection Screen" width="75%">

## Fields Explained

- **Server Address**: The hostname or IP of your RomM server.
- **Port**: Usually `443` for HTTPS.
- **Username** / **Password**: Your RomM credentials.  OIDC/OAuth is on the roadmap, but basic auth only for now.
- **Ignore Certificate**: Enable only if your server uses a self‑signed certificate.
