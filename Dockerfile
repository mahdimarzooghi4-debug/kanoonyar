FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY backend/src/Kanoonyar.Contracts/Kanoonyar.Contracts.csproj backend/src/Kanoonyar.Contracts/
COPY backend/src/Kanoonyar.Api/Kanoonyar.Api.csproj backend/src/Kanoonyar.Api/
RUN dotnet restore backend/src/Kanoonyar.Api/Kanoonyar.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/src/Kanoonyar.Api/Kanoonyar.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Kanoonyar.Api.dll"]
