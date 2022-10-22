# Set the base image as the .NET 6.0 SDK (this includes the runtime)
FROM mcr.microsoft.com/dotnet/sdk:6.0 as build-env

# Copy everything and publish the release (publish implicitly restores and builds)
WORKDIR /app
COPY . ./
RUN dotnet publish ./src/EAVFW.Extensions.GitHub.BlobStorageUploadArtifact.csproj -c Release -o out --no-self-contained

# Label the container
LABEL maintainer="EAVFW <info@eavfw.com>"
LABEL repository="https://github.com/eavfw/EAVFW.Extensions.GitHub.BlobStorageUploadArtifact"
LABEL homepage="https://eavfw.com/packages/EAVFW.Extensions.GitHub.BlobStorageUploadArtifact"

# Label as GitHub action
LABEL com.github.actions.name="Upload a Build Artifact to Azure Blob Storage"
# Limit to 160 characters
LABEL com.github.actions.description="Upload a build artifact that ca be used by subsequent workflow steps to Azure Blob Storage"
# See branding:
# https://docs.github.com/actions/creating-actions/metadata-syntax-for-github-actions#branding
LABEL com.github.actions.icon="sunrise"
LABEL com.github.actions.color="orange"

# Relayer the .NET SDK, anew with the build output
FROM mcr.microsoft.com/dotnet/sdk:6.0
COPY --from=build-env /app/out .
ENTRYPOINT [ "dotnet", "/EAVFW.Extensions.GitHub.BlobStorageUploadArtifact.dll" ]