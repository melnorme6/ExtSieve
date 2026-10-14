export const screenshotSources = {
  light: { file: 'main-light.png', width: 902, height: 732, sha256: 'd615eefb4f7fbad0679e8c30601bf036141fb3a92468f87f5650b9729668af44' },
  dark: { file: 'main-dark.png', width: 902, height: 732, sha256: '9d8efc3304b8523f08e85fd782cd6690cda05a4a2b84247c1ef943accc59946e' },
  filter: { file: 'How.png', width: 902, height: 732, sha256: '649873afd306e4a5407b82835665b7ab435c658e8ed6c287470a72f159da5189' },
} as const;

const archiveSave = { id: 'archive-save', left: 742, top: 623, width: 128, height: 40, alt: 'Save the ZIP archive.' } as const;

export const screenshotDetails = [
  { id: 'folder', source: 'light', left: 20, top: 79, width: 862, height: 119,
    alt: 'Folder path and scan counters: 65 files, 11 extensions and 22.1 MiB.',
    frames: [
      { id: 'folder-choice', left: 698, top: 79, width: 184, height: 38, alt: 'Choose another folder.' },
      { id: 'folder-path', left: 20, top: 79, width: 282, height: 119, alt: 'The selected C:\\Example folder and its 65-file counter.' },
    ] },
  { id: 'extensions', source: 'filter', left: 20, top: 207, width: 862, height: 164,
    alt: 'The cs filter is active. The .cs extension is selected; .csv is not selected.',
    frames: [
      { id: 'extensions-filter', left: 663, top: 207, width: 219, height: 38, alt: 'The extension filter contains cs.' },
      { id: 'extensions-selection', left: 20, top: 254, width: 282, height: 117, alt: 'The .cs checkbox is selected; .csv is not selected.' },
    ] },
  { id: 'archive', source: 'light', left: 20, top: 513, width: 862, height: 162,
    alt: 'Archive layout choices, ZIP output path and the Save button.',
    frames: [
      { id: 'archive-options', left: 154, top: 525, width: 347, height: 85, alt: 'Keep folder structure or put all files together, with C:\\Example.zip as output.' },
      archiveSave,
    ], mobileFrames: [
      { id: 'archive-preserve', left: 154, top: 525, width: 176, height: 38, alt: 'Keep folder structure is selected.' },
      { id: 'archive-flatten', left: 330, top: 525, width: 171, height: 38, alt: 'Put all files together is the alternative archive layout.' },
      { id: 'archive-output', left: 154, top: 572, width: 282, height: 38, alt: 'The ZIP output path is C:\\Example.zip.' },
      archiveSave,
    ] },
] as const;

export const screenshotCrops = [
  ...screenshotDetails,
  ...screenshotDetails.flatMap(detail => detail.frames.map(frame => ({ ...frame, source: detail.source }))),
  ...screenshotDetails.flatMap(detail => 'mobileFrames' in detail ? detail.mobileFrames.filter(frame => frame.id !== 'archive-save').map(frame => ({ ...frame, source: detail.source })) : []),
] as const;
