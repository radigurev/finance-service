FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config global.json ./
COPY local-packages/ local-packages/
COPY src/Directory.Build.props src/
COPY src/Finance.Common/ src/Finance.Common/
COPY src/Finance.GenericFiltering/ src/Finance.GenericFiltering/
COPY src/Finance.ServiceModel/ src/Finance.ServiceModel/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Databases/Finance.Journal.DBModel/ src/Databases/Finance.Journal.DBModel/
COPY src/Interfaces/Journal/Finance.Journal.API/ src/Interfaces/Journal/Finance.Journal.API/

RUN dotnet restore src/Interfaces/Journal/Finance.Journal.API/Finance.Journal.API.csproj

RUN dotnet publish src/Interfaces/Journal/Finance.Journal.API/Finance.Journal.API.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6004
ENTRYPOINT ["dotnet", "Finance.Journal.API.dll"]
