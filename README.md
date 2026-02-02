# Crunchyroll Metadata Plugin for Jellyfin

<p align="center">
  <img src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/icon-transparent.svg" alt="Jellyfin Logo" width="100">
</p>

A Jellyfin metadata plugin that fetches anime metadata directly from Crunchyroll, with intelligent season and episode mapping designed to match how most users organize their libraries.

🔗 **Leia em Português (Brasil): [README.pt-BR.md](README.pt-BR.md)**

---

## ✨ Features

- **Series Metadata**: Title, overview, release year, genres, and age rating
- **Season Metadata**: Season titles and descriptions
- **Episode Metadata**: Title, overview, runtime, and air date
- **Images**: Posters, backdrops, and episode thumbnails
- **Multi-language Support**: English, Portuguese (Brazil), Japanese, and more

---

## 🎯 Problems This Plugin Solves

### Split Seasons (AniDB-style behavior)

Some metadata providers treat each season as a separate series. This plugin avoids that by:

- Automatically mapping Jellyfin seasons to Crunchyroll seasons
- Keeping all seasons grouped under a single series entry

---

### Continuous Episode Numbering

Crunchyroll sometimes uses continuous episode numbering across seasons.

Example:

- **Jujutsu Kaisen**: Season 2 starts at episode 25 on Crunchyroll
- **Typical Jellyfin library**: Season 2 starts at episode 1

This plugin uses **automatic episode offset calculation**, ensuring:

- `S02E01` in Jellyfin → Crunchyroll Episode 25 ✅
- `S02E02` in Jellyfin → Crunchyroll Episode 26 ✅

---

## 📦 Installation

### Method 1: Plugin Repository (Recommended)

1. Open the Jellyfin Dashboard
2. Go to `Dashboard > Plugins > Repositories`
3. Click `+` and add the following manifest URL:

```
https://raw.githubusercontent.com/ocnaibill/crunchyroll-jellyfin/main/manifest.json
```

4. Save and go to `Dashboard > Plugins > Catalog`
5. Search for **Crunchyroll Metadata** and click **Install**
6. Restart Jellyfin

```bash
# Linux (systemd)
sudo systemctl restart jellyfin

# Docker
docker restart jellyfin
```

---

### Method 2: Manual Installation

1. Download `Jellyfin.Plugin.Crunchyroll.zip` from the Releases page
2. Extract the files to the appropriate plugins directory:

| OS | Path |
|----|------|
| Linux | `/var/lib/jellyfin/plugins/Crunchyroll/` |
| Windows | `C:\ProgramData\Jellyfin\Server\plugins\Crunchyroll\` |
| macOS | `~/.local/share/jellyfin/plugins/Crunchyroll/` |
| Docker | `/config/plugins/Crunchyroll/` |

> Create the `Crunchyroll` folder if it does not exist.

3. Restart Jellyfin

---

### Method 3: Build from Source

```bash
git clone https://github.com/ocnaibill/crunchyroll-jellyfin.git
cd crunchyroll-jellyfin
dotnet build -c Release
```

The compiled DLL will be located at:

```
Jellyfin.Plugin.Crunchyroll/bin/Release/net8.0/Jellyfin.Plugin.Crunchyroll.dll
```

Copy it to your Jellyfin plugins directory and restart the server.

---

## ⚙️ Configuration

Configure the plugin at:

```
Dashboard > Plugins > Crunchyroll Metadata
```

### Language Settings

- **Preferred Language**: Primary metadata language
- **Fallback Language**: Used when the preferred language is unavailable

### Season & Episode Mapping

- **Enable Season Mapping**: Maps Jellyfin seasons to Crunchyroll seasons
- **Enable Episode Offset Mapping**: Handles continuous episode numbering automatically

### Cache

- **Cache Expiration**: Metadata cache duration in hours (default: 24h)

---

## 🔧 Usage

### Anime Library Setup

1. Create or edit a TV Shows library
2. Set the content type to **Shows**
3. Enable **Crunchyroll** under:
   - Series Metadata Downloaders
   - Season Metadata Downloaders
   - Episode Metadata Downloaders
4. Enable **Crunchyroll** under Image Fetchers
5. Adjust provider priority as desired

---

### Recommended File Naming

```text
Animes/
├── Jujutsu Kaisen/
│   ├── Season 1/
│   │   ├── Jujutsu Kaisen - S01E01 - Ryomen Sukuna.mkv
│   │   └── ...
│   └── Season 2/
│       ├── Jujutsu Kaisen - S02E01 - Hidden Inventory.mkv
│       └── ...
```

---

### Manual Identification

If the plugin does not automatically match a series:

1. Open the series in Jellyfin
2. Click **Edit Metadata**
3. Select **Identify**
4. Search for the title on Crunchyroll
5. Choose the correct result and refresh metadata

---

## 🐛 Troubleshooting

### Series not found

- Ensure the series title matches Crunchyroll naming
- Use manual identification if needed
- Confirm the anime is available on Crunchyroll

### Incorrect language

- Verify language settings in the plugin configuration
- Some titles may not be localized in all languages

### Episode mismatch

- Ensure episode offset mapping is enabled
- Verify that each season starts at episode 1 locally

### Debug Logs

Enable debug logs in `Dashboard > Logs` and search for `Crunchyroll`.

---

## 🔄 Updates

When installed via the plugin repository, Jellyfin will automatically notify you when updates are available.

---

## 🤝 Contributing

Contributions are welcome!

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to your fork
5. Open a Pull Request

---

## 📄 License

This project is licensed under the MIT License. See `LICENSE.md` for details.

---

## ⚠️ Disclaimer

This plugin is not affiliated with, endorsed by, or sponsored by Crunchyroll or Sony.

Crunchyroll is a registered trademark of Sony Group Corporation.

This plugin only uses publicly available metadata and does not provide access to premium or copyrighted content.

---

## 🙏 Acknowledgements

- Jellyfin project and plugin developer community
- Unofficial Crunchyroll API documentation projects

<p align="center">
  Made with ❤️ for the Jellyfin community
</p>
