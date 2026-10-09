# syntax=docker/dockerfile:1
# Skanyxx Host + every module, for the Helm umbrella (deploy/helm/skanyxx). Build: scripts/helm/build-image.sh.
# No configuration or secrets in the image: everything comes from env (the chart's ConfigMap and Secrets).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/ src/
# Each module copies its dll + deps.json into src/Skanyxx.Host/modules/ after build (CopyToModules), as the release
# workflow relies on; twelve older modules resolve that folder from $(SolutionDir), which only a solution build sets,
# hence the property. Tests are not built. NuGet packages live in a BuildKit cache, so a
# source change does not download them again (the repo has no packages.lock.json for a locked restore, D144).
RUN --mount=type=cache,id=skanyxx-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet build src/Skanyxx.Host/Skanyxx.Host.csproj -c Release \
 && for p in src/Modules/*/*.csproj; do dotnet build "$p" -c Release --no-dependencies -p:SolutionDir=/src/ || exit 1; done \
 && dotnet publish src/Skanyxx.Host/Skanyxx.Host.csproj -c Release --no-build -o /out \
 && mkdir -p /out/modules \
 && cp src/Skanyxx.Host/modules/*.dll src/Skanyxx.Host/modules/*.deps.json /out/modules/

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# The leftover SQLite store (AppDbContext) lives at <content root>/skanyxx.db. The image is read-only at runtime, so
# that path points into /data (an emptyDir in the chart); SQLite follows the link and keeps its journal next to the
# real file. Company data is never here: identity, memory and tickets are Postgres (D060).
RUN mkdir /data && chown "$APP_UID" /data && ln -s /data/skanyxx.db /app/skanyxx.db
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Skanyxx.Host.dll"]
