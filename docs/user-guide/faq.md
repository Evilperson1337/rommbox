# FAQ

## Where do I open the RomM plugin?

Open **Tools → RomM** inside LaunchBox.

## Do I need to install the plugin separately?

No. Copy the plugin files into `LaunchBox\Plugins\RomMbox\` and restart LaunchBox.

## Does this work in Big Box?

The RomM menu items are not enabled for Big Box.

## Can I import games without downloading ROMs?

Yes. Use the **Import** action instead of **Install** on the Games page.

## Can I download save files from RomM?

Yes. Enable the **Saves** checkbox when importing games.

## Where are saves stored?

Saves are stored under:

- `LaunchBox\Saves\<Platform>`

## Can I upload saves back to RomM?

Uploading saves is not exposed in the UI.

## What does “Ignore Certificate” do?

It allows connections to servers with self‑signed or invalid TLS certificates.

## Why can’t I access Platforms or Games tabs?

You must configure and save a working connection first. The plugin locks other tabs until the server is configured.

## Where are settings stored?

Settings are stored in:

- `LaunchBox\Plugins\RomMbox\system\settings.json`
