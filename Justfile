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
