# JSON Schemas

توضع versioned schemas للـ OCR result،canonical retrieval،invoice revisions،posting lines وreconciliation. Gemini يدعم subset من JSON Schema،لذلك provider schema قد تكون projection أبسط من domain schema الكاملة.

Product label schema تفصل `rawLabel` و`canonicalLabel` و`labelSource` و`qualityFlags` و`displayDirection`. لا contract يسمح للـ client بإرسال heuristic-corrected label كأنه Genius source value.
