FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY src/Directory.Build.props src/
COPY src/Infrastructure/Gateway/Finance.Gateway/ src/Infrastructure/Gateway/Finance.Gateway/

RUN --mount=type=secret,id=github_user,env=GITHUB_PACKAGES_USER \
    --mount=type=secret,id=github_token,env=GITHUB_PACKAGES_TOKEN \
    dotnet restore src/Infrastructure/Gateway/Finance.Gateway/Finance.Gateway.csproj

RUN dotnet publish src/Infrastructure/Gateway/Finance.Gateway/Finance.Gateway.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6000
ENTRYPOINT ["dotnet", "Finance.Gateway.dll"]
