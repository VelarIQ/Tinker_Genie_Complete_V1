import React from 'react';

interface DictationPreviewProps {
  text: string;
  onAccept: () => void;
  onCancel: () => void;
  onEdit: (text: string) => void;
}

const DictationPreview: React.FC<DictationPreviewProps> = ({
  text,
  onAccept,
  onCancel,
  onEdit
}) => {
  return (
    <div className="absolute bottom-full left-0 right-0 mb-2 bg-white border border-gray-200 rounded-lg shadow-lg p-4 mx-2">
      <div className="mb-3">
        <label className="text-sm font-medium text-gray-700 mb-1 block">
          Dictated Text (edit if needed):
        </label>
        <textarea
          value={text}
          onChange={(e) => onEdit(e.target.value)}
          className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
          rows={3}
          autoFocus
        />
      </div>
      <div className="flex gap-2">
        <button
          onClick={onAccept}
          className="flex-1 px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 font-medium"
        >
          Send Message
        </button>
        <button
          onClick={onCancel}
          className="flex-1 px-4 py-2 bg-gray-200 text-gray-700 rounded-md hover:bg-gray-300 font-medium"
        >
          Cancel
        </button>
      </div>
    </div>
  );
};

export default DictationPreview;