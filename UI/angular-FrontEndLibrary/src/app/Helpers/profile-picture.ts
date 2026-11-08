// Turns any photo the user picks into a small square profile picture before
// it's uploaded: the middle square, scaled to 512 x 512, as WebP (or JPEG where
// the browser can't make WebP). A 6 MB phone photo becomes about 30-80 KB, well
// under the API's 512 KB limit. A crop tool where the user picks the square is #109.

export const PICTURE_SIZE = 512;
const MAX_BYTES = 512 * 1024;

export class PictureError extends Error {}

export async function prepareProfilePicture(file: File): Promise<{ blob: Blob; fileName: string }> {
  if (!file.type.startsWith('image/')) {
    throw new PictureError("That file isn't a picture.");
  }

  let bitmap: ImageBitmap;
  try {
    // Follows the photo's own rotation (EXIF), so phone photos aren't sideways
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    throw new PictureError("That picture couldn't be read. Try a PNG, JPEG or WebP.");
  }

  // The largest square in the middle
  const side = Math.min(bitmap.width, bitmap.height);
  const sx = (bitmap.width - side) / 2;
  const sy = (bitmap.height - side) / 2;

  const canvas = document.createElement('canvas');
  canvas.width = PICTURE_SIZE;
  canvas.height = PICTURE_SIZE;
  const context = canvas.getContext('2d')!;
  context.imageSmoothingQuality = 'high';
  context.drawImage(bitmap, sx, sy, side, side, 0, 0, PICTURE_SIZE, PICTURE_SIZE);
  bitmap.close();

  let blob = await toBlob(canvas, 'image/webp', 0.85);
  if (blob?.type !== 'image/webp') {
    blob = await toBlob(canvas, 'image/jpeg', 0.88);
  }
  // Very detailed pictures: try once more, smaller
  if (blob && blob.size > MAX_BYTES) {
    blob = await toBlob(canvas, 'image/jpeg', 0.7);
  }
  if (!blob || blob.size > MAX_BYTES) {
    throw new PictureError("That picture couldn't be made small enough. Try another one.");
  }

  const extension = blob.type === 'image/webp' ? 'webp' : 'jpg';
  return { blob, fileName: `profile-picture.${extension}` };
}

function toBlob(canvas: HTMLCanvasElement, type: string, quality: number): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality));
}
