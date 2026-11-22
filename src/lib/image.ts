// Shrinks bill photos on the phone before upload: faster on mobile data and
// still sharp enough to read. PDFs are sent as they are.
const MAX_LONG_SIDE = 2000;
const MAX_BYTES = 10 * 1024 * 1024;

export async function prepareBillFile(file: File): Promise<{ blob: Blob; ext: string; type: string }> {
  if (file.type === "application/pdf") {
    if (file.size > MAX_BYTES) throw new Error("That PDF is over 10 MB. Try a photo instead.");
    return { blob: file, ext: "pdf", type: "application/pdf" };
  }
  if (!file.type.startsWith("image/")) throw new Error("Please choose a photo or a PDF of the bill.");

  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file);
  } catch {
    // Format the browser can't decode (e.g. HEIC on some devices): upload as-is if it's a supported type.
    if (["image/jpeg", "image/png", "image/webp"].includes(file.type) && file.size <= MAX_BYTES) {
      return { blob: file, ext: file.type.split("/")[1].replace("jpeg", "jpg"), type: file.type };
    }
    throw new Error("This photo format isn't supported. Try taking the photo again or use a screenshot.");
  }

  const scale = Math.min(1, MAX_LONG_SIDE / Math.max(bitmap.width, bitmap.height));
  const width = Math.round(bitmap.width * scale);
  const height = Math.round(bitmap.height * scale);
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("Could not process the photo on this device.");
  ctx.drawImage(bitmap, 0, 0, width, height);
  bitmap.close();

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.85));
  if (!blob) throw new Error("Could not process the photo on this device.");
  return { blob, ext: "jpg", type: "image/jpeg" };
}
