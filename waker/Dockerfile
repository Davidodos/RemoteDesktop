# syntax=docker/dockerfile:1

# Multi-Arch: amd64 für die NAS, arm64 für einen Raspberry Pi an einem zweiten
# Standort. Bauen mit
#   docker buildx build --platform linux/amd64,linux/arm64 -f waker/Dockerfile .
# Das Image enthält nur JavaScript — es gibt nichts zu kompilieren, was pro
# Architektur unterschiedlich wäre.

# ---- Waker bauen ----------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:22-alpine AS build

WORKDIR /build

COPY waker/package*.json ./
RUN npm ci

COPY waker/tsconfig.json ./
COPY waker/src ./src
RUN npm run build

# ---- Laufzeit -------------------------------------------------------------
FROM node:22-alpine AS runtime

WORKDIR /app

ENV NODE_ENV=production

COPY waker/package*.json ./
RUN npm ci --omit=dev && npm cache clean --force

COPY --from=build /build/dist ./dist

# Die PWA wird hier **nicht** mehr ausgeliefert: es gibt eine APK und ein
# Windows-Fenster, und ein Webserver dafür wäre ein weiterer offener Dienst.
#
# Es gibt auch keine devices.json mehr. Die einzige Datei, die der Waker
# schreibt, ist clients.json — sie füllt sich beim Koppeln von selbst und
# enthält nur öffentliche Schlüssel.
ENV CLIENTS_PATH=/config/clients.json
ENV WAKER_PORT=3080
ENV BROADCAST_ADDRESS=255.255.255.255

EXPOSE 3080

CMD ["node", "dist/index.js"]
