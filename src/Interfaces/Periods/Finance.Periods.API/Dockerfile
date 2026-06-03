FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config global.json ./
COPY local-packages/ local-packages/
COPY src/Directory.Build.props src/
COPY src/Finance.Common/ src/Finance.Common/
COPY src/Finance.GenericFiltering/ src/Finance.GenericFiltering/
COPY src/Finance.ServiceModel/ src/Finance.ServiceModel/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Databases/Finance.Periods.DBModel/ src/Databases/Finance.Periods.DBModel/
COPY src/Interfaces/Periods/Finance.Periods.API/ src/Interfaces/Periods/Finance.Periods.API/

RUN dotnet restore src/Interfaces/Periods/Finance.Periods.API/Finance.Periods.API.csproj

RUN dotnet publish src/Interfaces/Periods/Finance.Periods.API/Finance.Periods.API.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6002
ENTRYPOINT ["dotnet", "Finance.Periods.API.dll"]
