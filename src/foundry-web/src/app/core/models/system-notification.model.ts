export interface SystemNotification {
  category: string;
  isActive: boolean;
  message: string;
}

export const CREDITS_NOTIFICATION_CATEGORY = 'credits';
export const DISPATCH_NOTIFICATION_CATEGORY = 'dispatch';
export const IMAGE_BUILD_NOTIFICATION_CATEGORY = 'image-build';
export const DOCKER_NOTIFICATION_CATEGORY = 'docker';
export const ACCOUNT_TOKEN_NOTIFICATION_CATEGORY = 'account-token';
export const PACKAGE_SOURCE_NOTIFICATION_CATEGORY = 'package-source';
export const ACCOUNT_TOKEN_EXPIRY_NOTIFICATION_CATEGORY = 'account-token-expiry';
