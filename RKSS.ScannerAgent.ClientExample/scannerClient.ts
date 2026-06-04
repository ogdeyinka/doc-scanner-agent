export async function scanToRkss({ rkssJwt, relatedModule, relatedRecordId, documentType }: {
  rkssJwt: string;
  relatedModule: string;
  relatedRecordId: string;
  documentType: string;
}) {
  const res = await fetch("http://127.0.0.1:8890/scan", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      dpi: 300,
      colorMode: "color",
      useAdf: true,
      duplex: false,
      maxPages: 25,
      outputFormat: "pdf",
      autoUpload: true,
      triggerOcr: true,
      rkssAuthToken: rkssJwt,
      relatedModule,
      relatedRecordId,
      documentType,
      backendUploadUrl: "http://localhost/rkss-api/scanned-documents/upload",
      ocrTriggerUrl: "http://localhost/rkss-api/scanned-documents/trigger-ocr"
    })
  });

  if (!res.ok) throw new Error(await res.text());
  return await res.json();
}
