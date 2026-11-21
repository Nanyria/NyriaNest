export interface ActionIconStyle {
  icon: string;      // e.g. Bootstrap, FontAwesome, or Material icon class
  styleClass: string; // CSS class for button styling
}

export const ACTION_ICON_MAP: { [label: string]: ActionIconStyle } = {
  'Borrow': { icon: 'assets/images/webpage-images/icons/borrowbook.svg', styleClass: 'action-borrow' },
  'Return': { icon: 'assets/images/webpage-images/icons/returnbook.svg', styleClass: 'action-return' },
  'Reserve': { icon: 'fa-solid fa-bookmark', styleClass: 'action-reserve' },
  'Cancel reservation': { icon: 'fa-solid fa-bookmark', styleClass: 'action-unreserve' },
  'Add to reading list': { icon: 'fa-solid fa-heart-circle-plus', styleClass: 'action-add-readlist' },
  'Remove from reading list': { icon: 'fa-solid fa-heart-circle-minus', styleClass: 'action-remove-readlist' },
  'Manage in My owned books': { icon: 'fa-solid fa-pen-to-square', styleClass: 'action-manage' },
  // Add more as needed
};