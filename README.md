# SRXDModManager
Command line mod manager for SRXD mods

## Config
If your game is not located at:
```
C:\Program Files (x86)\Steam\steamapps\common\Spin Rhythm
```
open config.json and replace the value of gameDirectory with the correct directory. Make sure the path consists of double backslashes (\\\\) instead of single backslashes (\\), like so:
```.json
{
	"gameDirectory": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Spin Rhythm"
}
```

## Commands

```
bepinex install        		Installs BepInEx

bepinex uninstall      		Uninstalls BepInEx
	--full, -f     		Uninstall all BepInEx files, including plugins and config

build il2cpp           		Switches SRXD to the IL2CPP build. Mods will not be loaded when using this build

build mono             		Switches SRXD to the Mono build. Mods will be loaded when using this build

check <name>           		Checks a mod for updates

check all              		Checks all mods for updates

download <repository>  		Downloads a mod from a Git release
	--dependencies, -d	Also download any missing dependencies for this mod

exit                   		Exits the application

info <name>            		Gets detailed information about a mod

info all               		Gets detailed information about all loaded mods

refresh                		Refreshes the list of downloaded mods

update <name>          		Updates a mod if there is a new version available
	--dependencies, -d	Also download any missing dependencies for this mod

update all             		Updates all loaded mods, if updates are available
	--dependencies, -d	Also download any missing dependencies for all mods
```
