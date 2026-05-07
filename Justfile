default:
    just --list

fmt:
    dotnet format

lint:
    dotnet restore
    dotnet format --verify-no-changes
    dotnet build --configuration Release --no-restore /warnaserror

test:
    dotnet test --configuration Release

run *args:
    dotnet run -- {{args}}

build-all-release:
    scripts/build.lash linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64

build-all-release-runtime:
    scripts/build.lash linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64 --no-self-contained

pack-binaries *args:
    scripts/pack.lash {{args}}

docs-web-build:
    cd docs/web && npm install && npm run build

docs-web-preview:
    cd docs/web && npm run preview

gen-package:
    just build-all-release-runtime
    just pack-binaries --suffix -runtime
    just build-all-release
    just pack-binaries --suffix -self-contained
    scripts/checksums.lash
