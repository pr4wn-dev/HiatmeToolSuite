# Deployment guide

The full checklist lives in **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)** (server bootstrap, desk setup, migration, daily ops, troubleshooting).

Quick start:

```powershell
# Office server
git clone https://github.com/pr4wn-dev/AIagent.git
cd AIagent
powershell -ExecutionPolicy Bypass -File scripts\bootstrap-new-server.ps1 -Role server

# Dispatch desk
git clone https://github.com/pr4wn-dev/HiatmeToolSuite.git
cd HiatmeToolSuite
powershell -ExecutionPolicy Bypass -File scripts\setup-desk.ps1 -OfficePanelUrl "http://<server-ip>:8787"
```
