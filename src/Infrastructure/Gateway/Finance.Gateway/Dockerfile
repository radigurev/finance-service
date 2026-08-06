FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY nuget.config global.json ./
COPY local-packages/ local-packages/
COPY src/Directory.Build.props src/
COPY src/Infrastructure/Gateway/Finance.Gateway/ src/Infrastructure/Gateway/Finance.Gateway/

RUN dotnet restore src/Infrastructure/Gateway/Finance.Gateway/Finance.Gateway.csproj

RUN dotnet publish src/Infrastructure/Gateway/Finance.Gateway/Finance.Gateway.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6000
ENTRYPOINT ["dotnet", "Finance.Gateway.dll"]
