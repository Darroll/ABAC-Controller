FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AbacController.sln ./
COPY src/ ./src/
COPY tests/ ./tests/

RUN dotnet restore AbacController.sln
RUN dotnet publish src/AbacController.Api/AbacController.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN adduser --disabled-password --gecos "" abac \
    && mkdir -p /data \
    && chown -R abac:abac /app /data

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
VOLUME ["/data"]
USER abac

ENTRYPOINT ["dotnet", "AbacController.Api.dll"]
