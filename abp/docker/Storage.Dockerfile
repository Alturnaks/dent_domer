FROM golang:1.25 AS build
ENV CGO_ENABLED=0
RUN go install github.com/minio/minio@RELEASE.2025-10-15T17-29-55Z
RUN go install github.com/minio/mc@RELEASE.2025-08-13T08-35-41Z

FROM debian:bookworm-slim AS minio
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=build /go/bin/minio /usr/local/bin/minio
COPY --from=build /go/pkg/mod/github.com/minio/minio@v0.0.0-20251015172955-9e49d5e7a648/LICENSE /usr/share/doc/minio/LICENSE
LABEL org.opencontainers.image.source="https://github.com/minio/minio" org.opencontainers.image.licenses="AGPL-3.0-only"
ENTRYPOINT ["minio"]

FROM debian:bookworm-slim AS mc
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=build /go/bin/mc /usr/local/bin/mc
COPY --from=build /go/pkg/mod/github.com/minio/mc@v0.0.0-20250813083541-7394ce0dd2a8/LICENSE /usr/share/doc/mc/LICENSE
LABEL org.opencontainers.image.source="https://github.com/minio/mc" org.opencontainers.image.licenses="AGPL-3.0-only"
ENTRYPOINT ["mc"]
