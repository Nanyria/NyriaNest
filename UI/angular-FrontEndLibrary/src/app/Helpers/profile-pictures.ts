// The pictures a user can choose without uploading their own (Settings → Profile).
// The API only accepts these paths, or the user's own upload, so they must match
// ProfilePictures.Presets in NyriaNest.Domain.

const FOLDER = 'assets/images/profile-pictures/';

export const PROFILE_PICTURES: { url: string; label: string }[] = [
  { url: `${FOLDER}RedDragon.png`, label: 'Red dragon' },
  { url: `${FOLDER}BlueDragon.png`, label: 'Blue dragon' },
  { url: `${FOLDER}GreenDragon.png`, label: 'Green dragon' },
];

// Shown for users who haven't picked a picture
export const DEFAULT_PROFILE_PICTURE = `${FOLDER}RedDragon.png`;
