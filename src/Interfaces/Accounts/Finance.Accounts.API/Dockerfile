FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY src/Directory.Build.props src/
COPY src/Finance.Common/ src/Finance.Common/
COPY src/Finance.ServiceModel/ src/Finance.ServiceModel/
COPY src/Databases/Finance.Accounts.DBModel/ src/Databases/Finance.Accounts.DBModel/
COPY src/Interfaces/Accounts/Finance.Accounts.API/ src/Interfaces/Accounts/Finance.Accounts.API/

RUN --mount=type=secret,id=github_user,env=GITHUB_PACKAGES_USER \
    --mount=type=secret,id=github_token,env=GITHUB_PACKAGES_TOKEN \
    dotnet restore src/Interfaces/Accounts/Finance.Accounts.API/Finance.Accounts.API.csproj

RUN dotnet publish src/Interfaces/Accounts/Finance.Accounts.API/Finance.Accounts.API.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 6001
ENTRYPOINT ["dotnet", "Finance.Accounts.API.dll"]
