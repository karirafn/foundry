export interface SystemNotification {
  category: string;
  isActive: boolean;
  message: string;
}

export const DISPATCH_NOTIFICATION_CATEGORY = 'dispatch';
export const IMAGE_BUILD_NOTIFICATION_CATEGORY = 'image-build';
