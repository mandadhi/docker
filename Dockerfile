FROM alpine:latest
WORKDIR /app
COPY . .
CMD ["cat", "README.md"]
