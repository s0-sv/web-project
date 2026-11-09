FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY src/TutorHub.Functions/TutorHub.Functions.csproj src/TutorHub.Functions/
COPY src/TutorHub.Integrations/TutorHub.Integrations.csproj src/TutorHub.Integrations/
RUN dotnet restore src/TutorHub.Functions/TutorHub.Functions.csproj
COPY src/TutorHub.Functions/ src/TutorHub.Functions/
COPY src/TutorHub.Integrations/ src/TutorHub.Integrations/
RUN dotnet publish src/TutorHub.Functions/TutorHub.Functions.csproj -c Release -o /home/site/wwwroot --no-restore

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
COPY --from=build /home/site/wwwroot /home/site/wwwroot
