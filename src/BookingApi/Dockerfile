FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/BookingApi/BookingApi.csproj src/BookingApi/
RUN dotnet restore src/BookingApi/BookingApi.csproj
COPY src/BookingApi/ src/BookingApi/
RUN dotnet publish src/BookingApi/BookingApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "BookingApi.dll"]
