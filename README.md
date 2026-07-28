<p align="center">
  <img src="avallama/Assets/Svg/avallama-logo.svg" alt="Avallama Logo" width="80">
</p>
<h1 align="center">Avallama</h1>

<p align="center">
  <a href="https://github.com/4foureyes/avallama/actions/workflows/pr-build.yml">
    <img alt="PR Build Badge" src="https://github.com/4foureyes/avallama/actions/workflows/pr-build.yml/badge.svg?branch=main">
  </a>
  <a href="https://github.com/4foureyes/avallama/actions/workflows/pr-test.yml">
    <img alt="PR Test Badge" src="https://github.com/4foureyes/avallama/actions/workflows/pr-test.yml/badge.svg?branch=main">
  </a>
  <a href="https://github.com/4foureyes/avallama/actions/workflows/continuous.yml">
    <img alt="Continuous Build Badge" src="https://github.com/4foureyes/avallama/actions/workflows/continuous.yml/badge.svg?branch=main">
  </a>
  <a href="https://codecov.io/gh/4foureyes/avallama">
    <img alt="Coverage Badge" src="https://codecov.io/gh/4foureyes/avallama/branch/main/graph/badge.svg">
  </a>
</p>


<p align="center">A cross-platform local AI desktop app powered by Ollama</p>

<p align="center">
    <img style="border-radius: 10px;" src="avallama/Assets/Images/home.png" alt="Avallama Home">
</p>

This project is currently being developed by Márk Csörgő and Martin Bartos.

## Features

- **Seamless Local LLMs** - Chat seamlessly with LLMs hosted locally on your machine
- **Your Data Stays With You** - Chats are saved to your machine, ensuring your data privacy
- **Ollama On The Network** – Connect the app to an Ollama instance on your network to easily use your AI workstation on any device
- **Multi-Platform Support** – Runs on Windows, Linux and macOS utilizing the Avalonia framework
- **Lightweight and Efficient** – Designed to be minimal while providing a smooth experience
- **Automatic Ollama Process Management** – Ensures Ollama runs efficiently in the background without manual intervention

## Contributions

We are currently not accepting outside contributions, however we encourage users to report bugs, crashes or any unexpected behaviour. Please see our [Contribution Guidelines](./CONTRIBUTING.md) for more information.

## Installation

**Important notice**: a working installation of Ollama is required for Avallama to work properly. Before installing Avallama on any platform, please download and install Ollama from [this link](https://ollama.com/download).

### Linux
#### Debian/Ubuntu (x64)

1. Download the latest .deb package from the [releases](https://github.com/4foureyes/avallama/releases) page.
2. Open a terminal and navigate to the directory where the file was downloaded, for example:

```bash
cd ~/Downloads
```

3. Install the package using the following command:

```bash
sudo apt install ./avallama_0.3.2_amd64.deb
```

*Replace `./avallama_0.3.2_amd64.deb` with the correct filename of the latest package*

4. After that, you can run the application from the application menu or with the `avallama` command

To uninstall, run:

```bash
sudo apt remove avallama
```

#### Arch Linux (x64)

1. Download the latest .pkg.tar.zst from the Releases tab.
2. Open a terminal and navigate to the directory where the file was downloaded.
3. Install the package using the following command:

```bash
sudo pacman -U avallama-0.3.2-1-x86_64.pkg.tar.zst
```

*Replace `avallama-0.3.2-1-x86_64.pkg.tar.zst` with the correct filename of the latest package*

To uninstall run:

```bash
sudo pacman -R avallama
```

To uninstall and remove config files

```bash
sudo pacman -Rns avallama
```

### Windows (x64)

The Windows installer is **not signed with a trusted code-signing certificate**, so Windows SmartScreen may prevent it from running. If you wish to circumvent this, click `More info -> Run anyway`.

1. Download the latest Windows installer from the [releases](https://github.com/4foureyes/avallama/releases) page, e.g. `avallama-setup-0.3.2.exe`.
2. Open the installer and go through the setup steps.
3. Once installed, you can run the application from the Start Menu.

To uninstall, remove it through the `Apps > Installed apps` page in Settings, or navigate to the installation directory and run unins000.exe.

### macOS (x64 and arm64)

This application is **not signed nor notarized**. MacOS may prevent it from running by default.

1. Download the latest `.dmg` file from the [releases](https://github.com/4foureyes/avallama/releases) page. (for Intel use *osx_x64*, for Apple Silicon use *osx_arm64*)
2. Open the downloaded `.dmg` file.
3. Drag **Avallama** into the **Applications** folder.

If the '*app is damaged or can't be opened*' error occurs, make sure you remove the quarantine flag using the following command in the Terminal:

```bash
xattr -cr /Applications/Avallama.app
```

## Building from source

Build instructions are available [here](./docs/Building.md).

## Known issues

A list of known issues can be found [here](./docs/KnownIssues.md).

## License

This project is licensed under the [MIT License](./LICENSE).
