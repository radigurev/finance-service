FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY nuget.config global.json ./
COPY local-packages/ local-packages/
COPY src/Directory.Build.props src/
COPY src/Finance.Common/ src/Finance.Common/
COPY src/Finance.GenericFiltering/ src/Finance.GenericFiltering/
COPY src/Finance.ServiceModel/ src/Finance.ServiceModel/
COPY src/Country/ src/Country/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Databases/Finance.EventLog.DBModel/ src/Databases/Finance.EventLog.DBModel/

RUN dotnet restore src/Infrastructure/EventLog/Finance.EventLog.API/Finance.EventLog.API.csproj

RUN dotnet publish src/Infrastructure/EventLog/Finance.EventLog.API/Finance.EventLog.API.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6008
ENTRYPOINT ["dotnet", "Finance.EventLog.API.dll"]
