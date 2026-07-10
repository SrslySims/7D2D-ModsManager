# Mods Manager

Mods Manager is an in-game mod management menu for 7 Days To Die. It lets players review installed mods, enable or disable mod folders before loading a save, see mod details, check manifest-based update notices, and edit supported mod config values from inside the game menu.

## Features

- Adds a Mods Manager button to the main menu.
- Lists active mods from `Mods` and disabled mods from `Mods_Disabled`.
- Enables and disables mods by moving folders between those locations.
- Shows mod name, display name, description, author, version, game version, website, icon, and banner when provided.
- Supports remote update manifest checks for mod version and game version notices.
- Supports creator-provided `ModsManagerConfig.xml` files for editable in-game settings.
- Supports simple key/value config files and XML-backed config files.
- Marks protected mods so required loader/manager mods cannot be disabled through the menu.

## Installation

1. Download the release archive.
2. Extract `0_ModsManager` into your 7 Days To Die `Mods` folder.
3. Start the game.
4. Use the Mods Manager button from the main menu.

A full game restart is required after enabling/disabling mods or saving config changes.

## For Mod Creators

Mods Manager can show richer information for your mod and can expose supported config options directly in-game. See the full creator guide:

[Creator Config Guide *See WIKI]([docs/CREATOR_CONFIG_GUIDE.md](https://github.com/SrslySims/7D2D-ModsManager/wiki))

## Folder Layout

```text
0_ModsManager/
  ModInfo.xml
  SrslyModsManager.dll
  Config/
    Localization.csv
    XUi_Menu/
      windows.xml
      xui.xml
  ModsManager/
    icon.png
    banner.png
    fallback_icon.png
    fallback_banner.png
```

## Notes

- Mods Manager does not download or install updates for users. It only reports update notices from creator-provided manifests.
- Mods Manager does not bypass the normal 7 Days To Die loading process. XML, DLL, and mod folder changes need a full restart.
- Mods with DLLs still need EAC disabled unless the mod author says otherwise.
