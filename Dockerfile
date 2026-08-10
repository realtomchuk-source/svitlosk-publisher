FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy Directory.Packages.props if it exists
COPY Directory.Packages.props .

# Copy projects and restore
COPY src/SvitloSk.Publisher.Domain/SvitloSk.Publisher.Domain.csproj src/SvitloSk.Publisher.Domain/
COPY src/SvitloSk.Publisher.Core/SvitloSk.Publisher.Core.csproj src/SvitloSk.Publisher.Core/
COPY src/SvitloSk.Publisher.Channels/SvitloSk.Publisher.Channels.csproj src/SvitloSk.Publisher.Channels/
COPY src/SvitloSk.Publisher.Execution/SvitloSk.Publisher.Execution.csproj src/SvitloSk.Publisher.Execution/
COPY src/SvitloSk.Publisher.Runtime/SvitloSk.Publisher.Runtime.csproj src/SvitloSk.Publisher.Runtime/
COPY src/SvitloSk.Publisher.Adapters.Telegram/SvitloSk.Publisher.Adapters.Telegram.csproj src/SvitloSk.Publisher.Adapters.Telegram/
COPY src/SvitloSk.Publisher.Host/SvitloSk.Publisher.Host.csproj src/SvitloSk.Publisher.Host/

RUN dotnet restore src/SvitloSk.Publisher.Host/SvitloSk.Publisher.Host.csproj

# Copy all files
COPY src/ src/

# Publish
RUN dotnet publish src/SvitloSk.Publisher.Host/SvitloSk.Publisher.Host.csproj -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Run as non-root user 'app'
USER app

# Copy published output
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Environment setup
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Health check using wget (available in alpine)
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "SvitloSk.Publisher.Host.dll"]
