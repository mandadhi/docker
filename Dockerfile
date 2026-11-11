# Optional: container build for local runs. For Datadog auto-instrumentation in a container you would
# additionally install the tracer (Datadog.Trace.Bundle / dd-trace-dotnet) and set the CORECLR_* variables.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DatadogObservability.Api/DatadogObservability.Api.csproj src/DatadogObservability.Api/
RUN dotnet restore src/DatadogObservability.Api
COPY src/ src/
RUN dotnet publish src/DatadogObservability.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DatadogObservability.Api.dll"]
