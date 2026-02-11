# playnite-successstory-plugin

Extension for [Playnite](https://playnite.link).

## Information

fork of [Lacro59/playnite-successstory-plugin](https://github.com/Lacro59/playnite-successstory-plugin) with added support for steam emulator GameDrive. also includes a tracking feature for hours played at achievement unlock. tested with their Mewgenics release, no idea if it works with any of their other releases but if it follows the same file structure it should


### building from source

to build from source, first clone the repo including the submodules:
```
git clone --recurse-submodules https://github.com/era-walk/playnite-successstory-plugin
```
(or if already cloned, run `git submodule update --init --recursive`)


you need MSBuild from [Microsoft Build Tools for Visual Studio](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022), or open `source/SuccessStory.sln` in Visual Studio and build that way.

if building from the command line with MSBuild, restore NuGet packages first:
```
nuget restore source/SuccessStory.sln
```
([download nuget.exe](https://www.nuget.org/downloads) if needed). opening the `.sln` in Visual Studio restores packages automatically.

then build the solution:
```
MSBuild source/SuccessStory.sln /p:Configuration=Release
```

the built extension will be in `source/bin/Release/`. zip that folder and rename the zip to `.pext`, then install in Playnite.