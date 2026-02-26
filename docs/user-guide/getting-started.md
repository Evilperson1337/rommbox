# Quick Start Guide
Use this guide to quickly get started with the RomMbox plugin.

> This guide already assumes that you have LaunchBox set up and configured.  While having Emulators with their associated platforms set up is not strictly necessary to have it work, it is recommended.

## Steps
1. Install the plugin into LaunchBox following the [Installation Guide](./installation-guide.md).
2. Open LaunchBox.


### Accessing the Plugin

1. Navigate to **Tools → RomM** to pop up the plugin configuration screen.

### Configure the Connection to the RomM Server
For more information about this screen see the [Connection Tab](./connection_tab.md) documentation.

1. Enter the RomM URL, either http or https (behind a reverse proxy) in the *Server Address* field.  
    e.g. *http://127.0.0.1*, *https://romm.domain.com*
2. Enter the port number to connect over.  
    e.g. *443*
3. Enter the username for the user you want to connect with.  
    e.g. *api_user*
4. Enter the password for the user entered above.  
    e.g. *super-password*
> If the server is secured with an untrusted (self-signed) certificate, click the **Ignore Certificate** checkbox.
5. Hit *Test* to test the connection is successful, then hit *Save* to save the connection.

### Configure the Platforms
For more information about this screen see the [Platforms Tab](./platforms_tab.md) documentation.

1. Click the **Platforms** tab 
2. Map the RomM platforms to the LaunchBox platforms, or exclude ones you don't want.
3. Click the **Configure** button for each platform you have selected and configure it using the [Platform Configuration Screen](./platforms_configure_tab.md).
4. Repeat for all platforms.
5. Review your selection and hit *Save*.

### Import Your Games
For more information about this screen see the [Games Tab](./games-tab.md) documentation.

1. Click the **Games** tab 
2. Select the platform you want to import from the platform drop down.
3. Wait for the games list to load.
4. Review the options, referencing the [Games Tab](./games-tab.md) if needed.
5. Click Import to begin the import process.
6. Repeat for all platforms.
7. Review your selection and hit *Save*.

## Viewing the Results
LaunchBox internally has a method of triggering the UI refresh functionality, but from what I have found there isn't a way to do it using the exposed plugin methods.  What this means is we will have to perform an action that triggers this internal call.  The easiest methods are:

- Close and restart LaunchBox
- Change to a different platform (if refreshing games on a platform)
- Change the display group selection (Platform, Platform Category, etc. - if you need to refresh the displayed platforms)