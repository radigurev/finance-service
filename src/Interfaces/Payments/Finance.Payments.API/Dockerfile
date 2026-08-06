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
COPY src/Databases/Finance.Payments.DBModel/ src/Databases/Finance.Payments.DBModel/
COPY src/Interfaces/Payments/Finance.Payments.API/ src/Interfaces/Payments/Finance.Payments.API/

RUN dotnet restore src/Interfaces/Payments/Finance.Payments.API/Finance.Payments.API.csproj

RUN dotnet publish src/Interfaces/Payments/Finance.Payments.API/Finance.Payments.API.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6006
ENTRYPOINT ["dotnet", "Finance.Payments.API.dll"]
