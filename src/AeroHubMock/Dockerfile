FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/AeroHubMock/AeroHubMock.csproj src/AeroHubMock/
RUN dotnet restore src/AeroHubMock/AeroHubMock.csproj
COPY src/AeroHubMock/ src/AeroHubMock/
RUN dotnet publish src/AeroHubMock/AeroHubMock.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "AeroHubMock.dll"]
