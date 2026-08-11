# Infrastructure

Infrastructure as Code للـ SaaS/Admin،وليس للـ pharmacy Genius instance.

- `environments/`: dev،staging،production composition.
- `modules/`: PostgreSQL،object storage،containers،KMS،network وDNS.
- `monitoring/`: dashboards،alerts وSLOs.
- `policies/`: retention،backup،access وrelease gates.

لا Kubernetes في baseline. يتم اختيار Terraform أوOpenTofu مرة واحدة ولا يُستخدم الاثنان معًا.
