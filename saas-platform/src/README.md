# SaaS Source Layout

يبدأ كـ modular monolith على .NET 10 مع OCR worker مستقلة process-wise. Modules تشترك في PostgreSQL لكن تملك schema/code boundaries واضحة. لا microservices إضافية قبل metrics تثبت الحاجة.
