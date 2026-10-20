# Windows version of build.sh: validates JSON, builds and packages Releases/walkers_<version>.zip.
# Needs the .NET 10 SDK and the VINTAGE_STORY environment variable set to your game folder
# (for example $env:APPDATA\Vintagestory). Usage: .\build.ps1 [--target=Build] [--configuration=Debug]
dotnet run --project CakeBuild/CakeBuild.csproj -- $args
exit $LASTEXITCODE;
