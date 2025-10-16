# SZ Extractor Server

A simple HTTP server for extracting **known (raw) files** and folders from Unreal Engine game data archives, specifically for **Dragon Ball: Sparking! Zero** but may work for many other games still. Based on [FModel](https://github.com/4sval/FModel/tree/master) and [CUE4Parse](https://github.com/FabianFG/CUE4Parse/tree/master).

Configure port in config.ini

## API Endpoints

### `POST /configure`

Configures the server with game data.

**Request Body:**
```json
{
  "gameDir": "C:\\Games\\Dragon Ball Example\\Paks",
  "engineVersion": "GAME_UE5_1",
  "aesKey": "0x1234...",
  "outputPath": "Output"
}
```
*   `gameDir`: **(Required)** Path to the game's `\Paks` directory.
*   `engineVersion`: **(Required)** Unreal Engine version (e.g., `GAME_UE5_1`).
*   `aesKey`: **(Required)** AES key in hex format (e.g., `0x1234...`).
*   `outputPath`: (Optional) Output directory. Defaults to Output.

**Response (200 OK):**
```json
{
  "Message": "Configuration updated, C:\\Games\\Dragon Ball Example\\Paks mounted",
  "MountedFiles": 12345
}
```
### `POST /extract`

Extracts a file or folder.

**Request Body:**
```json
{
  "contentPath": "SparkingZERO/Content/CriWareData/bgm_main.awb",
  "outputPath": "C:\\ExtractedFiles",
  "archiveName": "archive1"
}
```
*   `contentPath`: **(Required)** Virtual path/filename to extract (e.g., `SparkingZERO/Content/CriWareData/bgm_main.awb` or `bgm_main.awb`).
*   `outputPath`: (Optional) Output directory. Defaults to the one configured during `/configure`.
*   `archiveName`: (Optional) Name of the archive to extract from. If omitted, extraction will process all mounted archives.

**Response (201 Created):**
```json
{
  "Message": "Extraction successful",
  "FilePaths": [
    "C:\\ExtractedFiles\\archive1\\SparkingZERO\\Content\\CriWareData\\bgm_main.awb",
    "C:\\ExtractedFiles\\archive2\\SparkingZERO\\Content\\CriWareData\\bgm_main.awb"
  ]
}
```
Files will have been extracted following the format of the cli tool, paths are returned for convenience.

**Response (400 Bad Request):**
```json
{
  "Message": "Extraction failed",
  "FilePaths": [] 
}
```
### `GET /duplicates`

Retrieves a list of duplicate files and their locations.

**Response (200 OK):**
```json
{
  "SparkingZERO/Content/Characters/Goku/Goku_Base.uasset": [
    "archive1",
    "archive2"
  ],
  "SparkingZERO/Content/UI/MainMenu.umap": [
    "archive3",
    "archive4"
  ]
}
```
### `POST /dump`

Dumps all virtual file paths, filtered by a given path or pattern.

**Request Body:**
```json
{
  "filter": "Characters/Goku"
}
```
*   `filter`: **(Required)** A path or pattern to filter by. Supports regular expressions for pattern matching (e.g., `Characters/Goku` or `Characters.*\.awb$`). If an invalid or no regex pattern is provided, it falls back to simple string matching. The search is case-insensitive.

**Response (200 OK):**
```json
{
    "archive1": [
        "SparkingZERO/Content/Characters/Goku/Costumes/Base/Goku_Base.uasset",
        "SparkingZERO/Content/Characters/Goku/Blue/Goku_Blue.uasset"
    ],
    "archive2": [
        "SparkingZERO/Content/Characters/Goku/Costumes/Base/Goku_Base.uexp",
        "SparkingZERO/Content/Characters/Goku/Blue/Goku_Blue.uexp"
    ]
}
```
**Examples:**

## Notes

*   **Case-Insensitive:** Virtual paths are case-insensitive.
*   **Raw Data:** The server extracts raw data without any conversion.
*   **Duplicate Handling:** Duplicate files (same file in multiple archives) will be put inside a folder of their archive's name.
*   **Error Handling:** The server returns appropriate HTTP status codes (400, 500) for errors with response messages, check them if needed.
*   **Server must be configured before any other operation**

## Disclaimer

Mainly for Sparking Zero, Fmodel is a lot more robust and offers conversion features along with many other things, this is mainly for devs to use since I haven't seen a cli version of Fmodel
