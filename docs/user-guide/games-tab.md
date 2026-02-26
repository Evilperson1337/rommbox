# Games Tab

This tab will be for the handling and importing of your games into LaunchBox.

<img src="../_assets/images/romm_games_screen.png" alt="RomM Platforms Screen" width="75%">

## Window Breakdown

### Platform
Here is where you would select the platform for which you want to import games from.  Only platforms that have been mapped on the [Platforms Tab](./platforms_tab.md) will appear here.

### Duplicate Match
This option controls how the system determines a local game to be a duplicate of a RomM game.

- **Game Name**: Will try to match based on the Game's Title, the default option, and usually the fastest.  This is the recommended one most of the time.
- **File Name**: Will attempt to match based on the File Name.  This can be helpful for arcade games in particular where file names need to particular (e.g. *simpsons2a.zip*) and the Game Name matcher does not match it.
- **MD5**: Will calculate the MD5 hash of the game and compare it to the MD5 Hash of games listed in RomM.  This can take some time depending on the size of the library and whatnot and generally should be avoided unless necessary due to the performance overhead and potential issues depending on your configuration (like comparing an extracted game to an archive).

### Default Behavior
This controls what the default action the system will take for the games on the list, with the default being *Import All*.

- **Import All**: This will import stubbed entries of the games into LaunchBox with its accompanying image and metadata, but not actually download the game.  Useful when you want to display your entire library, but not have it locally.
- **Install All**: This will import the games with images and metadata AND download/install it locally - essentially making a 1-to-1 mirror of the platform in RomM.  For obvious reasons, this one will take the longest and use the most space (on account of downloading all the games).
- **Skip All**: This will skip importing the gamne, it will not appear in your LaunhBox library, nor will it download/install anything.  Does not apply to merge action items.

### Allow Duplicates
This would tell the system to allow for importing/installing previously imported games.  Currently it does nothing as I determine the actual necessity of such a feature.

### Hide Skipped Games
This option will hide the games that have the *Skip* action selected from the Games List below.  Useful if you only want to see items that will perform a meaningful action.

### Search Bar
This operates like you would expect a search bar to - you enter text and it will filter the Game List for titles that match.

### Refresh
Clicking this will refresh the Games List from the RomM Server.  You shouldn't really have to do this unless for some reason you add a game to the RomM server while this screen is open, but the option exists.

### Games List
Here you will find all of the Games for the platform on the RomM Server and what the system will do with each one.

#### Action
This determines what the system will do with the game.

- **Import**: Create a stubbed entry with metadata/images in LaunchBox but does not download/install the game.
- **Install**: Download the game alongside the metadata/images into LaunchBox, following the configuration for the platform.
- **Merge**: Associates a locally installed game with a RomM game, allowing the user to perform RomM actions with it - but does not download anything.
- **Skip**: Does not import the game in anyway.

#### Saves
This is a *To-Be-Implemented-Fully* feature, where it would download the save file(s) for the game hosted in RomM.  Checking the checkbox would enable it per game, checking it in the header would enable it for all games.  Currently it does not do anything as LaunchBox's Save Game functionality is in early access, but I am planning on introducing it in the future.

#### Games
This lists the title of the game and a note about the action it will take.