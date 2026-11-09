FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY dotnet-tools.json ./
COPY src/TutorHub/TutorHub.csproj src/TutorHub/
COPY src/TutorHub.Integrations/TutorHub.Integrations.csproj src/TutorHub.Integrations/
RUN dotnet restore src/TutorHub/TutorHub.csproj
COPY src/TutorHub/ src/TutorHub/
COPY src/TutorHub.Integrations/ src/TutorHub.Integrations/
RUN dotnet publish src/TutorHub/TutorHub.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false \
    && mkdir -p /app/storage

FROM build AS bundle
RUN dotnet tool restore \
    && ConnectionStrings__Default="Host=build;Database=build" \
       Jwt__SigningKey=build-time-placeholder-key-not-used-at-runtime \
       dotnet ef migrations bundle --project src/TutorHub/TutorHub.csproj --configuration Release \
        --self-contained --target-runtime linux-x64 --output /app/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra AS migrate
WORKDIR /app
COPY --from=bundle /app/efbundle ./efbundle
COPY --from=build /app/publish/appsettings.json ./
ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__LocalPath=/app/storage
COPY --from=build --chown=$APP_UID:$APP_UID /app/storage ./storage
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TutorHub.dll"]
