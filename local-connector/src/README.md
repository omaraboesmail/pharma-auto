# Local Connector Source Layout

يوضع implementation لاحقًا في projects المعزولة الموضحة في parent README. أهم قاعدة dependency: `Connector.Domain` لا يعرف Genius tables،و`Genius.Profile.Db539` لا يتسرب إلى Android/SaaS contracts.
