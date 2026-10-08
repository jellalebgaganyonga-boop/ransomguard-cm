# Runbook — Serveur GRID (production)

> Public visé : l'exploitant du serveur GRID (mise en service, déploiement,
> incident, sauvegarde). Suppose un accès SSH au serveur et un accès
> administrateur au dépôt GitHub et à la console Tailscale.

## 1. Ce qui tourne, et où

| Élément | Valeur |
|---|---|
| Hôte | `ubuntu2204` — Ubuntu 24.04 LTS, 2 vCPU, 3,8 Go RAM |
| Adresse Tailscale | `100.85.208.124` |
| Nom DNS (MagicDNS) | `ubuntu2204.tail6494ba.ts.net` |
| Console (dashboard) | `https://ubuntu2204.tail6494ba.ts.net:8443/` |
| Canal agents (mTLS) | `https://ubuntu2204.tail6494ba.ts.net/` (port 443) |
| Utilisateur de déploiement | `ubuntu` |

Quatre conteneurs Docker : `grid-mysql`, `grid-redis`, `grid-api`, `grid-nginx`.
Tous en `restart: unless-stopped`, et le service `docker` est activé au
démarrage — **la stack remonte seule après un redémarrage du serveur**.

### Deux certificats, deux rôles — ne pas les confondre

| Port | Certificat | Pourquoi |
|---|---|---|
| `:8443` console | **Let's Encrypt** émis par `tailscale cert` | cadenas vert dans le navigateur, aucun avertissement |
| `:443` agents | **PKI interne** (`shared/pki`) | les agents s'authentifient par certificat client signé par cette CA — une CA publique ne peut pas jouer ce rôle |

Remplacer la PKI interne du port 443 par un certificat public casserait
l'authentification mutuelle de tous les agents.

### Arborescence sur le serveur

```
/opt/ransomguard/
├── app/                      arbre de sources, remplacé à chaque déploiement
│   └── deployment/grid/      → .env et pki sont des liens vers shared/
├── shared/                   ÉTAT PERSISTANT — jamais écrasé
│   ├── .env                  secrets générés (chmod 600)
│   ├── pki/                  CA racine, CA intermédiaire, cert serveur, clé Ed25519
│   ├── tls/                  certificat Let's Encrypt de la console
│   ├── dashboard-dist/       bundle React servi par nginx
│   └── agent-packages/       install.ps1 + ZIP de l'agent téléchargés par les postes
├── backups/                  dumps SQL + secrets (14 jours)
└── logs/
```

## 2. Mise en service d'un serveur neuf

```bash
# Prérequis : Docker, Tailscale connecté, /opt/ransomguard créé
cd /opt/ransomguard/app/deployment/grid
./bootstrap.sh "Hopital Central Yaounde" CM
```

`bootstrap.sh` est idempotent. Il génère la PKI (avec l'IP et le nom MagicDNS
dans les SAN), les secrets, le certificat de la console, et installe les timers
systemd (renouvellement TLS + sauvegarde nocturne). Il n'écrase jamais un `.env`
ni une PKI existants.

Prérequis côté Tailscale, **une seule fois** :
console → **DNS** → **HTTPS Certificates** → *Enable*.
Sans cette option, `tailscale cert` échoue et la console retombe sur la PKI
interne (avertissement navigateur, mais service fonctionnel).

## 3. Déploiement

### Automatique (nominal)

`push` sur `main` → workflow **CI** → s'il est vert, workflow **CD** :

1. build du bundle dashboard et du paquet agent Windows ;
2. le runner GitHub rejoint le tailnet comme nœud éphémère (`tag:ci`) ;
3. `rsync` des sources, du bundle et du paquet ;
4. `deploy.sh` sur le serveur ;
5. vérification de santé depuis l'extérieur de la machine.

`deploy.sh` applique les migrations Alembic (entrypoint de l'image), rafraîchit
le certificat, attend le *health check*, rejoue la seed, lance 4 tests de fumée,
et **restaure automatiquement l'image précédente** si l'un d'eux échoue.

### Manuel

```bash
ssh ubuntu@100.85.208.124
cd /opt/ransomguard/app/deployment/grid && ./deploy.sh
```

C'est le même script que celui de la CI : un déploiement manuel ne peut pas
diverger d'un déploiement automatique.

## 4. Secrets GitHub à créer

`Settings → Secrets and variables → Actions → New repository secret`

| Secret | Contenu |
|---|---|
| `DEPLOY_HOST` | `100.85.208.124` |
| `DEPLOY_USER` | `ubuntu` |
| `DEPLOY_SSH_KEY` | clé privée `~/.ssh/rg/rg_ci_ed25519` (contenu complet) |
| `DEPLOY_KNOWN_HOSTS` | sortie de `ssh-keyscan -t ed25519 100.85.208.124` |
| `TS_OAUTH_CLIENT_ID` | client OAuth Tailscale |
| `TS_OAUTH_SECRET` | secret du même client |

### Client OAuth Tailscale

1. https://login.tailscale.com/admin/settings/oauth → **Generate OAuth client**
2. Scope : **Auth Keys → Write**, tag `tag:ci`
3. La politique d'accès (`Access controls`) doit déclarer le tag et l'autoriser
   à joindre le serveur :

```jsonc
"tagOwners": { "tag:ci": ["autogroup:admin"] },
"acls": [
  { "action": "accept", "src": ["tag:ci"], "dst": ["100.85.208.124:22"] }
]
```

## 5. Enrôlement d'un poste Windows

Console → **Agents** → **Add Agent** (rôle `tenant_admin`) → deux commandes à
coller sur le poste, en administrateur :

```powershell
curl.exe -k -o C:\inst.ps1 https://ubuntu2204.tail6494ba.ts.net:8443/agent/install.ps1
powershell -EP Bypass -File C:\inst.ps1 -OTP <otp> -GridServer ubuntu2204.tail6494ba.ts.net
```

Le poste doit être membre du tailnet. L'OTP est à usage unique.

## 6. Exploitation courante

```bash
# état
docker ps
docker compose --project-name grid --env-file /opt/ransomguard/shared/.env ps

# journaux
docker logs -f grid-api
docker logs --tail 100 grid-nginx

# sauvegarde immédiate
/opt/ransomguard/app/deployment/grid/backup.sh

# renouvellement TLS immédiat
/opt/ransomguard/app/deployment/grid/refresh-tls.sh && docker exec grid-nginx nginx -s reload

# tâches planifiées
systemctl list-timers 'ransomguard-*'
```

### Restauration d'une base

```bash
cd /opt/ransomguard
set -a; . shared/.env; set +a
zcat backups/db-<horodatage>.sql.gz | docker exec -i grid-mysql \
    mysql -u root -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"
```

### Retour arrière d'une version

`deploy.sh` le fait seul en cas d'échec des tests de fumée. Manuellement :

```bash
docker images ransomguard/grid-api          # repérer l'image précédente
docker tag <id> ransomguard/grid-api:local
docker compose --project-name grid --env-file /opt/ransomguard/shared/.env up -d --no-build grid-api
```

## 7. À sauvegarder hors machine

`shared/pki/ca.key`, `shared/pki/intermediate.key`,
`shared/pki/threat-intel-ed25519.key`, `shared/.env`.

Perdre `ca.key` impose de réenrôler **tous** les agents. Les sauvegardes
nocturnes restent sur la même machine : un incident rançongiciel sur ce serveur
emporterait les sauvegardes avec lui — copiez `backups/` sur un support externe.

## 8. Pannes fréquentes

| Symptôme | Cause probable | Action |
|---|---|---|
| `deploy.sh` échoue à l'étape 1 | valeur non quotée contenant une espace dans `.env` | le script affiche la ligne fautive ; entourer la valeur de guillemets |
| Avertissement de certificat sur la console | HTTPS Certificates désactivé côté Tailscale | activer l'option, puis `./refresh-tls.sh` |
| Agent ne s'enrôle pas | poste hors du tailnet, ou OTP déjà consommé | vérifier `tailscale status`, régénérer un OTP |
| `grid-api` en boucle de redémarrage | migration Alembic en échec | `docker logs grid-api`, corriger, redéployer |
| Serveur injoignable | VM éteinte | rallumer : la stack remonte seule |
