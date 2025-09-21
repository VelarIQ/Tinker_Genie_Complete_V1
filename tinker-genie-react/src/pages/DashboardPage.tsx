import { useAppSelector } from '../hooks/redux';
import { FiUser, FiActivity, FiTarget, FiTrendingUp } from 'react-icons/fi';

export default function DashboardPage() {
  const { user } = useAppSelector((state) => state.auth);
  const { dailyPrompt } = useAppSelector((state) => state.chat);

  const stats = [
    {
      icon: <FiActivity className="text-primary-600" />,
      label: 'Current Day',
      value: dailyPrompt.dayNumber || 1,
      change: '+1 from yesterday',
    },
    {
      icon: <FiTarget className="text-green-600" />,
      label: 'Daily Prompts Completed',
      value: dailyPrompt.isComplete ? 1 : 0,
      change: 'Today',
    },
    {
      icon: <FiTrendingUp className="text-blue-600" />,
      label: 'Total Conversations',
      value: 27,
      change: '+3 this week',
    },
    {
      icon: <FiUser className="text-purple-600" />,
      label: 'Leadership Score',
      value: '85%',
      change: '+5% improvement',
    },
  ];

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Header */}
      <div className="bg-white shadow">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="py-6">
            <h1 className="text-3xl font-bold text-gray-900">
              Dashboard
            </h1>
            <p className="mt-1 text-gray-600">
              Welcome back, {user?.firstName || 'Leader'}!
            </p>
          </div>
        </div>
      </div>

      {/* Main Content */}
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        {/* Stats Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
          {stats.map((stat, index) => (
            <div key={index} className="card">
              <div className="flex items-center justify-between mb-4">
                <div className="p-2 bg-gray-50 rounded-lg">
                  {stat.icon}
                </div>
                <span className="text-xs text-gray-500">
                  {stat.change}
                </span>
              </div>
              <h3 className="text-2xl font-bold text-gray-900">
                {stat.value}
              </h3>
              <p className="text-sm text-gray-600 mt-1">
                {stat.label}
              </p>
            </div>
          ))}
        </div>

        {/* Recent Activity */}
        <div className="card">
          <h2 className="text-lg font-semibold mb-4">Recent Activity</h2>
          <div className="space-y-4">
            <div className="flex items-center justify-between pb-4 border-b">
              <div className="flex items-center space-x-3">
                <div className="w-2 h-2 bg-green-500 rounded-full"></div>
                <div>
                  <p className="text-sm font-medium">Daily Prompt Completed</p>
                  <p className="text-xs text-gray-500">Day {dailyPrompt.dayNumber || 1} reflection submitted</p>
                </div>
              </div>
              <span className="text-xs text-gray-500">2 hours ago</span>
            </div>
            <div className="flex items-center justify-between pb-4 border-b">
              <div className="flex items-center space-x-3">
                <div className="w-2 h-2 bg-red-500 rounded-full"></div>
                <div>
                  <p className="text-sm font-medium">Burning Fire Resolved</p>
                  <p className="text-xs text-gray-500">Staff scheduling crisis handled</p>
                </div>
              </div>
              <span className="text-xs text-gray-500">Yesterday</span>
            </div>
            <div className="flex items-center justify-between">
              <div className="flex items-center space-x-3">
                <div className="w-2 h-2 bg-blue-500 rounded-full"></div>
                <div>
                  <p className="text-sm font-medium">Knowledge Base Search</p>
                  <p className="text-xs text-gray-500">Found 3 resources on member retention</p>
                </div>
              </div>
              <span className="text-xs text-gray-500">2 days ago</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
