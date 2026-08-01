# Deployment

*(Translates `DEPLOYMENT_ARCHITECTURE.md` into physical instructions).*

This repository will provide Dockerfiles and Kubernetes manifests to deploy the Runtime.

## Topology Mapping
The specification defines physical capacity scaling. This implementation achieves this via:
- **Worker Capacity:** Horizontal scaling of the `publisher-worker` Docker container.
- **Dispatcher Capacity:** Horizontal scaling of the `publisher-dispatcher` container.
- **Persistence:** A managed PostgreSQL cluster.
- **Coordination:** A Redis cluster.
