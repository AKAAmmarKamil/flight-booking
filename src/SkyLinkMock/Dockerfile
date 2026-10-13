FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/SkyLinkMock/SkyLinkMock.csproj src/SkyLinkMock/
RUN dotnet restore src/SkyLinkMock/SkyLinkMock.csproj
COPY src/SkyLinkMock/ src/SkyLinkMock/
RUN dotnet publish src/SkyLinkMock/SkyLinkMock.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "SkyLinkMock.dll"]
