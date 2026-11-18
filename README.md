# SwanShell_ImageBuilder

**SwanShell Image Builder** is a free Windows tool that creates the per-game images shown by the
**swanshell / SwanShellDX** menu of the **nileswan** flash cartridge for the Bandai WonderSwan /
WonderSwan Color.

It takes the game names from your ROM folder and the pictures from your image folders (or from the
internet), converts them to the exact format the menu reads, and writes them into the right folders
on the SD card. It never overwrites or deletes anything already on the card.

```
<SD card>\NILESWAN\IMAGES\<ROM name without extension>\1.bmp ... 6.bmp   224x144, 4 bpp, 16 colours
<SD card>\NILESWAN\IMAGES\<ROM name without extension>\thumb.bmp         112x72,  4 bpp, 16 colours
```

## Building

No installation needed: the program is a single C# source file built with the `csc.exe` of the
.NET Framework 4, which ships with Windows 7/8/10/11.

1. Download or clone this repository.
2. Double-click **`build.bat`**.
3. Run **`SwanShellImageBuilder.exe`** (a single portable file).

If `build.bat` says `csc.exe` was not found, enable ".NET Framework 4.8" in
*Turn Windows features on or off*.

## Usage

1. **ROM folder**: only the file names are used (the list of extensions is editable).
   For `Game.wsc.bps` the folder is `Game.wsc`, the first name the menu looks for.
2. **Image folder**: PNG, JPG, BMP, GIF, TIFF, subfolders included — for example a scraper `media`
   folder with `images`, `mix` and `screenshot` inside.
3. **Destination**: with *create the NILESWAN\IMAGES structure* enabled, pick the SD card root.
4. **Preview** lists every game without writing anything. Select a game to see:
   - **Already on the card**: the `.bmp` files already in its folder, their position in the menu
     (only the first 6 are shown) and a warning for files the menu would ignore
     (wrong size, not 4-bit, too large…);
   - **New images**: exactly as they will be written, with the file name each one will get or the
     reason it will not be written. The thumbnail shows in orange the 2-pixel frame the menu draws
     over it.
5. **Build images** writes the files; details are in the *Log* tab.

Changing the fit mode, dithering or zoom redraws the selected game immediately.
All options, folders and the window layout are saved when the program closes and restored at the
next start (in `SwanShellImageBuilder.ini` next to the exe, or in `%APPDATA%\SwanShellImageBuilder`
if that folder is not writable).

## How images are matched

An image belongs to a game when its name (without extension) is:

- the same as the ROM name, also in different subfolders
  (e.g. libretro `Named_Snaps\Game.png`, `Named_Titles\Game.png`, `Named_Boxarts\Game.png`);
- or the ROM name + separator + number or tag: `Game_1.png`, `Game-2.jpg`, `Game 3.png`,
  `Game_title`, `Game_snap`, `Game_cover`, `Game_box`, `Game_thumb`…

A hex hash at the end of the name (`Game (English) 9B23BCDCC67ED02A.png`, typical of scrapers) is
ignored. With *loose matching*, when no folder image matches exactly, the text in `()` and `[]`,
case, spaces and punctuation are ignored (useful for translated or patched ROMs).

With *one image per subfolder*, each game takes one image from each subfolder. In
*Thumbnail … from:* you choose the subfolder the thumbnail is made from.

Thumbnail priority: `*_thumb` image → the subfolder chosen in *from:* → a cover
(`*_cover`, `*_box`, or a folder with "box"/"cover" in its name) → the first image.

## Images from the internet

Select a game in the Preview tab and click **Search images online…** (or double-click the game):

- **Bing Images**: free-text search inside the program (default query: game name + "WonderSwan").
- **libretro thumbnails**: exact No-Intro names only.
- **ScreenScraper**: looks the game up by ROM checksum (CRC32, MD5, SHA1, name and size) on
  WonderSwan Color (46) and WonderSwan (45), then by name; shows screenshots, title screens, boxes,
  cartridge, logos, mixes… One request at a time; quota errors are shown and stop the search.
  Needs **developer credentials** (requested on the ScreenScraper forum) and optionally your user
  account, entered in *ScreenScraper account…*; passwords are encrypted with Windows DPAPI.
  Data and media courtesy of [ScreenScraper.fr](https://www.screenscraper.fr).
- **Google Images**: Google has no usable API, so *Open Google Images* opens the search in your
  browser; drag the picture into the window, or copy it and press Ctrl+V (*Paste image*).

Click a result to see it converted, then **Add as full-screen image** or **Use as thumbnail**.
Picked images are saved as PNG in the `_web` subfolder of the image folder
(`_web\<game>_1.png`, `_web\<game>_thumb.png`) and are then used like any other image.
Each downloaded image has a delete link in the preview.

## Existing images are never overwritten or deleted

- New full-screen images get the first free names (`1.bmp`, `2.bmp`… skipping names in use).
- An image identical to one already in the folder is not written again, so running *Build* twice
  adds nothing.
- Nothing is added once the folder has 6 full-screen images (the menu shows only 6).
- `thumb.bmp` is written only if the folder has none.
- Deleting is up to you: in the Preview tab each existing file has a *Delete…* link and the folder
  has *Delete all of them…*; both ask for confirmation.

## Conversion details

- **Crop to fill** fills the screen and crops the excess; **Fit** keeps the whole image with black
  bars (for the thumbnail the subject stays inside the central 108x68 pixels).
- Screenshots already 224x144 (or 448x288, 672x432…) are copied pixel by pixel; with 16 colours
  or fewer the conversion is lossless.
- Reduction to 16 colours: median cut + k-means refinement, optional Floyd-Steinberg dithering.
- BMP format: uncompressed 4 bpp, 16-entry palette (B, G, R, 0), rows bottom-up, left pixel in the
  high nibble, file under 65,535 bytes. Every file is re-checked with the same tests the menu
  performs before it is written.

Command line (like the original `ws_bmp.py` script):

```
SwanShellImageBuilder.exe full  screenshot.png 1.bmp
SwanShellImageBuilder.exe thumb cover.png      thumb.bmp
```

## Antivirus warnings

Some antivirus programs may flag the freshly built `SwanShellImageBuilder.exe`, for example
Malwarebytes with **`MachineLearning/Anomalous`**.

### What the detection means

`MachineLearning/Anomalous` is not a malware signature. Malwarebytes' machine-learning engine
compares a file with the known-good software it was trained on, and reports it as *anomalous* when
it does not look like anything it knows. It does not mean the file is malicious. Here it happens
because:

- the exe is compiled on your own PC a moment before, so no antivirus has ever seen that exact file;
- it is not digitally signed;
- it is a small, new .NET program that reads folders, writes files and downloads images.

The program is not packed or obfuscated, and it carries consistent version information
(description, author, copyright, version), as Malwarebytes recommends to developers. The whole
program is the single source file in this repository, which you can read before building it.

### Check it on VirusTotal

1. Open [virustotal.com](https://www.virustotal.com) and choose **File**.
2. Upload `SwanShellImageBuilder.exe` and wait for the scan.
3. One or two generic detections ("ML", "Heuristic", "Generic", "Anomalous") among about 70 engines
   are typical false positives. Many engines reporting a specific, named threat would not be.

### Exclude it from your antivirus

Exclude only the exe (or the folder where you build it), not whole drives.

- **Malwarebytes**: in the detection window choose **Ignore always**; or *Settings → Allow List →
  Add → Allow a file or folder* and pick the exe.
- **Microsoft Defender**: *Windows Security → Virus & threat protection → Manage settings →
  Exclusions → Add or remove exclusions → Add an exclusion → File*. If the exe was already
  quarantined, restore it from *Protection history* first.
- Other antivirus programs have a similar "exclusions" or "allow list" setting.

Remember that rebuilding with `build.bat` creates a new file, which may be flagged again.

### Report it as a false positive

- **Malwarebytes**: open a new thread in the
  [File Detections forum](https://forums.malwarebytes.com/forum/42-file-detections/), attaching the
  exe in a ZIP and naming the detection.
- **Microsoft Defender**: [submit the file](https://www.microsoft.com/en-us/wdsi/filesubmission) as
  *Incorrectly detected as malware/malicious*.
- **Other engines** seen on VirusTotal: each vendor has its own false-positive form, usually linked
  from its support pages.

## License

GPL-3.0 — see [LICENSE](LICENSE).
