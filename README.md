# Copernicus DEM Downloader

A command-line tool for downloading Copernicus Digital Elevation Model (DEM) tiles from the Copernicus Data Space Ecosystem (CDSE) S3 storage.

## Features

- **Interactive and batch modes** - Guided setup or scriptable automation
- **Geographic filtering** - Download only tiles within a bounding box
- **Resumable downloads** - Automatically skip already downloaded files
- **Parallel downloads** - Configurable concurrent downloads for faster transfers
- **Multiple datasets** - Support for GLO-30, GLO-90, and EEA-10 datasets
- **Mask selection** - Download DEM, EDM, FLM, HEM, or WBM files
- **Dry-run mode** - Preview files before downloading
- **Auto-discovery** - Automatically finds available datasets and versions

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- A CDSE account with S3 credentials from [Copernicus Data Space Ecosystem](https://dataspace.copernicus.eu/)

### Getting S3 Credentials

1. Register at [dataspace.copernicus.eu](https://dataspace.copernicus.eu/)
2. Go to your profile settings
3. Generate S3 credentials (Access Key and Secret Key)

## Installation

### Build from source

```bash
git clone https://github.com/yourusername/CopernicusDemDownloader.git
cd CopernicusDemDownloader/src/CopernicusDemDownloader
dotnet build -c Release
```

The executable will be at `bin/Release/net9.0/CopernicusDemDownloader.exe` (Windows) or `bin/Release/net9.0/CopernicusDemDownloader` (Linux/macOS).

### Run directly

```bash
cd src/CopernicusDemDownloader
dotnet run -- [options]
```

## Usage

### Interactive Mode

Simply run the tool without credentials to start interactive mode:

```bash
CopernicusDemDownloader
```

This will guide you through:
1. Entering S3 credentials
2. Selecting a dataset (GLO-30, GLO-90, EEA-10)
3. Selecting a version
4. Choosing mask types (DEM, EDM, FLM, etc.)
5. Setting output directory
6. Optionally specifying a bounding box
7. Configuring parallelism

### Batch Mode

For scripting and automation, use batch mode with credentials:

```bash
# Using command-line options
CopernicusDemDownloader --batch \
    --access-key YOUR_ACCESS_KEY \
    --secret-key YOUR_SECRET_KEY \
    --dataset GLO-30 \
    --bbox -10,35,30,70 \
    --output ./dem_europe

# Using environment variables
export CDSE_ACCESS_KEY=your_access_key
export CDSE_SECRET_KEY=your_secret_key
CopernicusDemDownloader --batch --dataset GLO-90 --bbox 10,45,11,46
```

### Examples

Download GLO-30 DEM tiles for Europe:
```bash
CopernicusDemDownloader --batch --dataset GLO-30 --bbox -10,35,30,70 --output ./europe_dem
```

Download GLO-90 tiles for a small area with 8 parallel downloads:
```bash
CopernicusDemDownloader --batch --dataset GLO-90 --bbox 10,45,12,47 --parallel 8
```

Preview files without downloading:
```bash
CopernicusDemDownloader --batch --dataset GLO-30 --bbox 10,45,11,46 --dry-run
```

Download DEM and water body mask files:
```bash
CopernicusDemDownloader --batch --dataset GLO-30 --bbox 10,45,11,46 --masks DEM,WBM
```

Resume an interrupted download:
```bash
CopernicusDemDownloader --batch --dataset GLO-30 --bbox 10,45,11,46 --output ./my_download
# Run the same command again - already downloaded files will be skipped
```

Force re-download of existing files:
```bash
CopernicusDemDownloader --batch --dataset GLO-30 --bbox 10,45,11,46 --force
```

## CLI Options

| Option | Description | Default |
|--------|-------------|---------|
| `--interactive` | Force interactive mode | Auto-detect |
| `--batch` | Non-interactive batch mode | `false` |
| `--access-key` | S3 access key | `CDSE_ACCESS_KEY` env var |
| `--secret-key` | S3 secret key | `CDSE_SECRET_KEY` env var |
| `--output` | Output directory | `./CopDEM_<dataset>` |
| `--dataset` | Dataset: EEA-10, GLO-30, GLO-90 | `GLO-30` |
| `--version-year` | Dataset version (e.g., 2024_1) | Latest |
| `--prefix` | Custom S3 prefix (overrides --dataset) | - |
| `--bbox` | Bounding box: minLon,minLat,maxLon,maxLat | None (all tiles) |
| `--masks` | Mask types: DEM,EDM,FLM,HEM,WBM | `DEM` |
| `--parallel` | Number of parallel downloads | CPU cores |
| `--retries` | Max retry attempts per file | `3` |
| `--state-file` | State file name for resume | `download_state.json` |
| `--dry-run` | List files without downloading | `false` |
| `--force` | Re-download existing files | `false` |
| `--endpoint` | S3 endpoint URL | CDSE endpoint |
| `--bucket` | S3 bucket name | `eodata` |

## Available Datasets

| Dataset | Resolution | Coverage | Description |
|---------|------------|----------|-------------|
| GLO-30 | 30m | Global | Copernicus DEM GLO-30 (DGED format) |
| GLO-30-DTED | 30m | Global | Copernicus DEM GLO-30 (DTED format) |
| GLO-90 | 90m | Global | Copernicus DEM GLO-90 (DGED format) |
| GLO-90-DTED | 90m | Global | Copernicus DEM GLO-90 (DTED format) |
| EEA-10 | 10m | Europe | High-resolution European DEM (requires CCM access) |

Add `-PUBLIC` suffix for public variants (e.g., `GLO-30-PUBLIC`).

## Mask Types

| Mask | Description |
|------|-------------|
| DEM | Digital Elevation Model (height data) |
| EDM | Editing Mask |
| FLM | Filling Mask |
| HEM | Height Error Mask |
| WBM | Water Body Mask |

## Environment Variables

| Variable | Description |
|----------|-------------|
| `CDSE_ACCESS_KEY` | S3 access key (alternative to --access-key) |
| `CDSE_SECRET_KEY` | S3 secret key (alternative to --secret-key) |

## Performance Tips

- **Use bounding box**: Always specify `--bbox` to avoid scanning the entire dataset (500k+ files)
- **Adjust parallelism**: Use `--parallel` to balance speed vs. system resources
- **Resume support**: Re-run the same command to resume interrupted downloads

## License

Apache 2.0 License

## Acknowledgments

- Data provided by the [Copernicus Data Space Ecosystem](https://dataspace.copernicus.eu/)
- Copernicus DEM is produced by the European Space Agency (ESA)
