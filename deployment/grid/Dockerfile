FROM python:3.12-slim AS builder

WORKDIR /build
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential libssl-dev \
    && rm -rf /var/lib/apt/lists/*

COPY grid/requirements.txt ./
RUN pip install --no-cache-dir --user -r requirements.txt

FROM python:3.12-slim AS runtime

RUN groupadd -r ransomguard --gid 1000 && \
    useradd -r -g ransomguard --uid 1000 --no-create-home --home-dir /app ransomguard

WORKDIR /app
COPY --from=builder --chown=ransomguard:ransomguard /root/.local /home/ransomguard/.local
COPY grid/src /app/src
COPY grid/alembic /app/alembic
COPY grid/alembic.ini /app/

ENV PATH=/home/ransomguard/.local/bin:$PATH
ENV PYTHONPATH=/app/src
ENV PYTHONUSERBASE=/home/ransomguard/.local

RUN mkdir -p /var/lib/ransomguard-grid/threat-intel-packages && \
    chown -R ransomguard:ransomguard /app /var/lib/ransomguard-grid

USER ransomguard
EXPOSE 8000

CMD ["sh", "-c", "alembic upgrade head && uvicorn ransomguard_grid.main:app --host 0.0.0.0 --port 8000 --workers 2 --proxy-headers --forwarded-allow-ips='*'"]
